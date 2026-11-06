using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using ShiftManager.Api;
using ShiftManager.Data;
using ShiftManager.Domain;
using ShiftManager.Services.AutomaticScheduling;
using Priority = ShiftManager.Domain.SchedulingPriority;

namespace ShiftManager.Tests;

public sealed partial class SchedulingTests
{
    private static UpdateShiftTypesRequest PriorityRequest(ShiftTypeResponse[] rows) => new(rows.Select(r =>
        new ShiftTypeUpdate(r.Id, r.Version, r.Name, r.DisplayOrder, r.Period, r.RequiredEmployees,
            r.IncludeInAutomaticScheduling, r.TimeRange, r.RequiresShiftLeadQualification, r.SchedulingPriority)).ToArray());

    [PostgresFact]
    public async Task Priority_configuration_is_admin_only_validated_persisted_and_used_by_future_runs()
    {
        using var admin = await Login(); using var employee = await Login("employee");
        var request = PriorityRequest(await Types(admin));
        var updates = request.ShiftTypes.Select(r => r with { RequiredEmployees = r.Id == BoardDefinition(2) || r.Id == BoardDefinition(26) ? 1 : 0,
            IncludeInAutomaticScheduling = r.Id == BoardDefinition(2) || r.Id == BoardDefinition(26),
            SchedulingPriority = r.Id == BoardDefinition(26) ? "Critical" : "Normal", DisplayOrder = 52 - r.DisplayOrder }).ToArray();
        request = new(updates);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(employee, TypesPath, request)).StatusCode);
        foreach (var invalid in new[] { "Urgent", "critical", "1", "" })
        {
            var bad = updates.ToArray(); bad[0] = bad[0] with { SchedulingPriority = invalid };
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(admin, TypesPath, new UpdateShiftTypesRequest(bad))).StatusCode);
        }
        Assert.Equal(HttpStatusCode.OK, (await Send(admin, TypesPath, request)).StatusCode);
        var saved = await Types(admin);
        Assert.Equal("Critical", saved.Single(r => r.Slot == 26).SchedulingPriority);
        Assert.Equal("Normal", saved.Single(r => r.Slot == 2).SchedulingPriority);
        var run = await Generate(admin); Assert.Equal("READY", run.State);
        Assert.NotNull(run.Stages); Assert.Equal(6, run.Stages.Length);
        Assert.True(run.CoverageOptimal);
        var first = new DateOnly(2026, 10, 1);
        Assert.Single(run.Cells.Single(c => c.DefinitionId == BoardDefinition(26) && c.Date == first).Employees);
        Assert.Empty(run.Cells.Single(c => c.DefinitionId == BoardDefinition(2) && c.Date == first).Employees);
        var change = PriorityRequest(saved).ShiftTypes.Select(r => r with { SchedulingPriority = r.Id == BoardDefinition(2) ? "Critical" : "High" }).ToArray();
        Assert.Equal(HttpStatusCode.OK, (await Send(admin, TypesPath, new UpdateShiftTypesRequest(change))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(admin, $"{GenerationPath}/{run.Id}/save", new { }, "POST")).StatusCode);
        var next = await Generate(admin); Assert.Equal("READY", next.State);
        Assert.Single(next.Cells.Single(c => c.DefinitionId == BoardDefinition(2) && c.Date == first).Employees);
        await using var restarted = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Development")
            .UseSetting("ConnectionStrings:Database", database.GetConnectionString()));
        using var scope = restarted.Services.CreateScope();
        var rows = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Set<ShiftTypeDefinition>().AsNoTracking().ToArrayAsync();
        Assert.Equal(Priority.Critical, rows.Single(r => r.Slot == 2).SchedulingPriority);
        Assert.Equal(Priority.High, rows.Single(r => r.Slot == 26).SchedulingPriority);
    }

    [PostgresFact]
    public async Task Priority_changes_and_reorders_preserve_published_snapshot_bytes_and_omitted_values()
    {
        var (admin, definition, review) = await ManualSetup(); using (admin)
        {
            review = await Edit(admin, review, "ASSIGN", ids["employee"], definition, 1); review = await Ack(admin, review);
            var publication = await Publish(admin, review, "PRELIMINARY");
            string before;
            using (var scope = factory.Services.CreateScope()) before = (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
                .Set<SchedulePublication>().SingleAsync(p => p.Id == publication.Id)).Snapshot;
            var rows = await Types(admin);
            var updates = PriorityRequest(rows).ShiftTypes.Select(r => r with { DisplayOrder = 52 - r.DisplayOrder,
                SchedulingPriority = r.Id == definition ? "High" : r.SchedulingPriority }).ToArray();
            Assert.Equal(HttpStatusCode.OK, (await Send(admin, TypesPath, new UpdateShiftTypesRequest(updates))).StatusCode);
            // Legacy client sends no priority; ordinary edits must not erase it.
            Assert.Equal(HttpStatusCode.OK, (await Send(admin, TypesPath, Request(await Types(admin)))).StatusCode);
            Assert.Equal("High", (await Types(admin)).Single(r => r.Id == definition).SchedulingPriority);
            using var final = factory.Services.CreateScope();
            Assert.Equal(before, (await final.ServiceProvider.GetRequiredService<ApplicationDbContext>().Set<SchedulePublication>().SingleAsync(p => p.Id == publication.Id)).Snapshot);
        }
    }

    [PostgresFact]
    public async Task Priority_migration_uses_stable_IDs_preserves_configuration_and_is_idempotent()
    {
        var (admin, definition, review) = await ManualSetup(); using var adminClient = admin;
        review = await Edit(admin, review, "ASSIGN", ids["employee"], definition, 1); review = await Ack(admin, review);
        var publication = await Publish(admin, review, "PRELIMINARY");
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var snapshot = (await db.Set<SchedulePublication>().SingleAsync(p => p.Id == publication.Id)).Snapshot;
        var assignments = await db.Set<ShiftAssignment>().Select(a => a.Id).OrderBy(id => id).ToArrayAsync();
        await db.GetService<IMigrator>().MigrateAsync("20261104135940_OperationalControls");
        await db.Database.ExecuteSqlRawAsync("UPDATE \"ShiftTypeDefinition\" SET \"DisplayOrder\" = 52 - \"DisplayOrder\", \"Name\" = 'Previously edited', \"RequiredEmployees\" = 3, \"Version\" = 7");
        var users = await db.Users.CountAsync();
        await db.Database.MigrateAsync(); db.ChangeTracker.Clear();
        var rows = await db.Set<ShiftTypeDefinition>().OrderBy(r => r.Slot).ToArrayAsync();
        Assert.Equal(51, rows.Length); Assert.Equal(users, await db.Users.CountAsync());
        Assert.Equal(snapshot, (await db.Set<SchedulePublication>().SingleAsync(p => p.Id == publication.Id)).Snapshot);
        Assert.Equal(assignments, await db.Set<ShiftAssignment>().Select(a => a.Id).OrderBy(id => id).ToArrayAsync());
        Assert.All(rows, r => { Assert.Equal(r.Slot <= 7 ? Priority.Critical : Priority.Normal, r.SchedulingPriority);
            Assert.Equal(52 - r.Slot, r.DisplayOrder); Assert.Equal("Previously edited", r.Name); Assert.Equal(3, r.RequiredEmployees); Assert.Equal(7, r.Version); });
        rows[25].SchedulingPriority = Priority.High; await db.SaveChangesAsync();
        await db.Database.MigrateAsync(); db.ChangeTracker.Clear();
        Assert.Equal(Priority.High, (await db.Set<ShiftTypeDefinition>().SingleAsync(r => r.Slot == 26)).SchedulingPriority);
        var exception = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE \"ShiftTypeDefinition\" SET \"SchedulingPriority\" = 'Urgent' WHERE \"Slot\" = 26"));
        Assert.Equal(Npgsql.PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
