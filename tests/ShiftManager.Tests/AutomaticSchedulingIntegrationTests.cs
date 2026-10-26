using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ShiftManager.Services.AutomaticScheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ShiftManager.Api;
using ShiftManager.Data;
using ShiftManager.Domain;

namespace ShiftManager.Tests;
public sealed partial class SchedulingTests
{
    private const string GenerationPath = "/api/admin/scheduling/generation";
    private async Task<Guid[]> ConfigureAutomatic(params int[] requirements)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var rows = await db.Set<ShiftTypeDefinition>().OrderBy(x => x.Slot).ToArrayAsync();
        foreach (var r in rows) { r.IncludeInAutomaticScheduling = false; r.RequiredEmployees = 0; r.Version++; }
        for (var i = 0; i < requirements.Length; i++) { rows[i].IncludeInAutomaticScheduling = true; rows[i].RequiredEmployees = requirements[i]; rows[i].Period = ShiftType.DAY; rows[i].TimeRange = ShiftTimeRange.DAY; }
        await db.SaveChangesAsync();
        return rows.Select(x => x.Id).ToArray();
    }
    private async Task<GenerationResponse> Generate(HttpClient client)
    {
        var response = await Send(client, GenerationPath, new { year = 2026, month = 10 }, "POST");
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var run = (await response.Content.ReadFromJsonAsync<GenerationResponse>())!;
        for (var i = 0; i < 300; i++)
        {
            run = (await client.GetFromJsonAsync<GenerationResponse>($"{GenerationPath}/{run.Id}"))!;
            if (run.State is not ("QUEUED" or "RUNNING")) return run;
            await Task.Delay(50);
        }
        throw new TimeoutException("Generation did not complete");
    }
    [PostgresFact]
    public async Task Automatic_preview_has_no_writes_explicit_save_roundtrips_snapshot_and_regeneration()
    {
        var definitions = await ConfigureAutomatic(3, 1, 0);
        using var client = await Login();
        var run = await Generate(client);
        Assert.Equal("READY", run.State); Assert.False(run.PublicationReady);
        Assert.Equal(124, run.Summary!.Required); Assert.Equal(31, run.Summary.Filled); Assert.Equal(93, run.Summary.Missing);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Empty(await db.Set<ShiftAssignment>().ToArrayAsync());
            Assert.Empty(await db.Set<SchedulingRun>().ToArrayAsync());
            Assert.Empty(await db.Set<SchedulePeriod>().ToArrayAsync());
        }
        Assert.Equal(HttpStatusCode.OK, (await Send(client, $"{GenerationPath}/{run.Id}/save", new { }, "POST")).StatusCode);
        var board = (await client.GetFromJsonAsync<WeeklyBoardResponse>("/api/admin/scheduling/board?date=2026-10-01"))!;
        Assert.Contains(board.Rows.SelectMany(x => x.Cells), c => c.Employees?.Length > 0 && c.State == "SAVED_DRAFT");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(31, await db.Set<ShiftAssignment>().CountAsync());
            Assert.All(await db.Set<SchedulePeriod>().ToArrayAsync(), p => Assert.Equal(ScheduleStatus.DRAFT, p.Status));
            Assert.Empty(await db.Set<SchedulePublication>().ToArrayAsync());
            Assert.DoesNotContain(await db.Set<SchedulingCell>().ToArrayAsync(), c => c.ShiftTypeDefinitionId == definitions[2]);
            var row = await db.Set<ShiftTypeDefinition>().SingleAsync(d => d.Id == definitions[0]); row.RequiredEmployees = 5; row.Version++;
            await db.SaveChangesAsync();
            Assert.All(await db.Set<SchedulingCell>().Where(c => c.ShiftTypeDefinitionId == row.Id).ToArrayAsync(), c => Assert.Equal(3, c.RequiredEmployees));
        }
        var next = await Generate(client); Assert.Equal(186, next.Summary!.Required);
        Assert.Equal(HttpStatusCode.OK, (await Send(client, $"{GenerationPath}/{next.Id}/save", new { }, "POST")).StatusCode);
        using var finalScope = factory.Services.CreateScope();
        Assert.Equal(31, await finalScope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Set<ShiftAssignment>().CountAsync());
    }
    [PostgresFact]
    public async Task Automatic_cancel_stale_configuration_and_account_changes_do_not_write_assignments()
    {
        var definitions = await ConfigureAutomatic(1);
        using var client = await Login();
        var canceled = await Generate(client);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(client, $"{GenerationPath}/{canceled.Id}", new { }, "DELETE")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(client, $"{GenerationPath}/{canceled.Id}/save", new { }, "POST")).StatusCode);
        var run = await Generate(client);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await db.Set<ShiftTypeDefinition>().SingleAsync(d => d.Id == definitions[0]); row.RequiredEmployees++; row.Version++;
            await db.SaveChangesAsync();
        }
        var stale = await Send(client, $"{GenerationPath}/{run.Id}/save", new { }, "POST");
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("STALE_PREVIEW", (await stale.Content.ReadFromJsonAsync<ApiError>())!.Code);
        run = await Generate(client);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.Users.SingleAsync(u => u.Id == ids["employee"])).IsActive = false; await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await Send(client, $"{GenerationPath}/{run.Id}/save", new { }, "POST")).StatusCode);
        var inactive = await Generate(client); Assert.Equal(0, inactive.Summary!.Filled);
        using var last = factory.Services.CreateScope();
        Assert.Empty(await last.ServiceProvider.GetRequiredService<ApplicationDbContext>().Set<ShiftAssignment>().ToArrayAsync());
    }
    [PostgresFact]
    public async Task Automatic_excluded_locked_manual_and_lead_assignments_remain_unchanged()
    {
        var definitions = await ConfigureAutomatic(1);
        var fixedIds = new List<Guid>();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var period = new SchedulePeriod { Year = 2026, Month = 10 };
            var oldRun = new SchedulingRun { Id = Guid.NewGuid(), Year = 2026, Month = 10, SavedByUserId = ids["admin"], SavedAt = DateTime.UtcNow };
            db.AddRange(period, oldRun);
            for (var i = 1; i <= 4; i++)
            {
                var date = new DateOnly(2026, 10, i); var time = ShiftClock.Boundaries(date, ShiftType.DAY);
                var shift = new Shift { SchedulePeriodId = period.Id, ShiftDate = date, StartsAt = time.StartsAt, EndsAt = time.EndsAt };
                var cell = new SchedulingCell { ShiftTypeDefinitionId = definitions[i == 4 ? 1 : 0], SchedulePeriodId = period.Id, SchedulingRunId = oldRun.Id, Date = date, StartsAt = time.StartsAt, EndsAt = time.EndsAt, RequiredEmployees = 1, ConfigurationVersion = 1 };
                var member = new ShiftAssignment { Shift = shift, Cell = cell, UserId = ids["employee"], IsLocked = i == 1, IsShiftLead = i == 2, GeneratedByRunId = i == 3 ? null : oldRun.Id };
                fixedIds.Add(member.Id); db.Add(member);
            }
            await db.SaveChangesAsync();
        }
        using var client = await Login();
        var run = await Generate(client); Assert.Equal("READY", run.State);
        Assert.Equal(3, run.Cells.SelectMany(c => c.Employees).Count(e => e.Protected));
        Assert.Equal(HttpStatusCode.OK, (await Send(client, $"{GenerationPath}/{run.Id}/save", new { }, "POST")).StatusCode);
        using var final = factory.Services.CreateScope(); var context = final.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(4, await context.Set<ShiftAssignment>().CountAsync(a => fixedIds.Contains(a.Id)));
        Assert.Equal(1, (await context.Set<SchedulingCell>().SingleAsync(c => c.ShiftTypeDefinitionId == definitions[1])).RequiredEmployees);
    }
    [PostgresFact]
    public async Task Automatic_failed_save_rolls_back_all_changes()
    {
        await ConfigureAutomatic(1); using var client = await Login(); var run = await Generate(client);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION reject_test_assignment() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'test rejection'; END $$;
            CREATE TRIGGER reject_test_assignment BEFORE INSERT ON "ShiftAssignment" FOR EACH ROW EXECUTE FUNCTION reject_test_assignment();
            """);
        Assert.Equal(HttpStatusCode.InternalServerError, (await Send(client, $"{GenerationPath}/{run.Id}/save", new { }, "POST")).StatusCode);
        Assert.Empty(await db.Set<SchedulingCell>().ToArrayAsync()); Assert.Empty(await db.Set<SchedulingRun>().ToArrayAsync());
        Assert.Empty(await db.Set<SchedulePeriod>().ToArrayAsync()); Assert.Empty(await db.Set<ShiftAssignment>().ToArrayAsync());
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_test_assignment ON \"ShiftAssignment\"; DROP FUNCTION reject_test_assignment();");
        Assert.Equal(HttpStatusCode.OK, (await Send(client, $"{GenerationPath}/{run.Id}/save", new { }, "POST")).StatusCode);
    }
    [PostgresFact]
    public async Task Automatic_endpoints_require_administrator_and_csrf()
    {
        using var anon = Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync($"{GenerationPath}/{Guid.NewGuid()}")).StatusCode);
        using var employee = await Login("employee"); using var coordinator = await Login("coordinator");
        foreach (var client in new[] { employee, coordinator })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(client, GenerationPath, new { year = 2026, month = 10 }, "POST")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"{GenerationPath}/{Guid.NewGuid()}/shortages")).StatusCode);
        }
        using var admin = await Login();
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(admin, GenerationPath, new { year = 2026, month = 10 }, "POST", false)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(admin, GenerationPath, new { year = 2026, month = 13 }, "POST")).StatusCode);
    }
    [PostgresFact]
    public async Task Automatic_blocks_submitted_preferences_and_concurrent_save_are_revalidated()
    {
        await ConfigureAutomatic(1);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var period = new SchedulePeriod { Year = 2026, Month = 10 };
            var times = ShiftClock.Boundaries(new(2026, 10, 1), ShiftType.DAY);
            var shift = new Shift { SchedulePeriod = period, ShiftDate = new(2026, 10, 1), StartsAt = times.StartsAt, EndsAt = times.EndsAt };
            db.AddRange(new AvailabilitySubmission { UserId = ids["employee"], SchedulePeriod = period, Version = 1, LastSubmittedAt = DateTime.UtcNow },
                new ShiftPreference { UserId = ids["employee"], Shift = shift, PreferenceType = PreferenceType.BLOCKED });
            await db.SaveChangesAsync();
        }
        using var client = await Login(); var run = await Generate(client);
        Assert.Equal("READY", run.State); Assert.Equal(30, run.Summary!.Filled);
        Assert.Empty(run.Cells.Single(c => c.Date == new DateOnly(2026, 10, 1)).Employees);
        var saves = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Send(client, $"{GenerationPath}/{run.Id}/save", new { }, "POST")));
        Assert.Single(saves, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(saves, r => r.StatusCode == HttpStatusCode.Conflict);
    }

    [PostgresFact]
    public async Task Automatic_custom_range_database_hard_block_backstop_checks_previous_night()
    {
        var definitions = await ConfigureAutomatic(1);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.Set<ShiftTypeDefinition>().SingleAsync(d => d.Id == definitions[0]); row.TimeRange = ShiftTimeRange.H06_09; row.Version++;
        await db.SaveChangesAsync();
        using var client = await Login(); var run = await Generate(client);
        Assert.Equal(HttpStatusCode.OK, (await Send(client, $"{GenerationPath}/{run.Id}/save", new { }, "POST")).StatusCode);
        db.ChangeTracker.Clear();
        var period = new SchedulePeriod { Year = 2026, Month = 9 };
        var times = ShiftClock.Boundaries(new(2026, 9, 30), ShiftType.NIGHT);
        var shift = new Shift { SchedulePeriod = period, ShiftDate = new(2026, 9, 30), ShiftType = ShiftType.NIGHT, StartsAt = times.StartsAt, EndsAt = times.EndsAt };
        db.Add(shift); await db.SaveChangesAsync();
        db.Add(new ShiftPreference { UserId = ids["employee"], ShiftId = shift.Id, PreferenceType = PreferenceType.BLOCKED });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    private sealed class NoSolutionOptimizer(bool fail) : IScheduleOptimizer
    {
        public OptimizationResult Solve(SchedulingProblem problem, double seconds, CancellationToken ct) => fail
            ? throw new InvalidOperationException("test technical failure") : new("TIMEOUT", 0, []);
    }
    [PostgresFact]
    public async Task Automatic_timeout_and_technical_failure_cannot_be_saved()
    {
        await ConfigureAutomatic(1);
        foreach (var fail in new[] { false, true })
        {
            await using var alternate = factory.WithWebHostBuilder(b => b.ConfigureServices(s => {
                s.RemoveAll<IScheduleOptimizer>(); s.AddSingleton<IScheduleOptimizer>(new NoSolutionOptimizer(fail));
            }));
            using var client = alternate.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
            Assert.Equal(HttpStatusCode.OK, (await Send(client, "/api/auth/login", new { username = "admin", password = Password }, "POST")).StatusCode);
            var run = await Generate(client);
            Assert.Equal(fail ? "FAILED" : "TIMEOUT", run.State); Assert.Null(run.Summary); Assert.Empty(run.Cells);
            Assert.Equal(HttpStatusCode.Conflict, (await Send(client, $"{GenerationPath}/{run.Id}/save", new { }, "POST")).StatusCode);
        }
        using var scope = factory.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Set<ShiftAssignment>().ToArrayAsync());
    }
    [PostgresFact]
    public async Task Automatic_new_submitted_hard_block_makes_preview_stale()
    {
        await ConfigureAutomatic(1);
        Guid shiftId; Guid periodId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var period = new SchedulePeriod { Year = 2026, Month = 10 };
            var times = ShiftClock.Boundaries(new(2026, 10, 1), ShiftType.DAY);
            var shift = new Shift { SchedulePeriod = period, ShiftDate = new(2026, 10, 1), StartsAt = times.StartsAt, EndsAt = times.EndsAt };
            db.Add(shift); await db.SaveChangesAsync(); shiftId = shift.Id; periodId = period.Id;
        }
        using var client = await Login(); var run = await Generate(client);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.AddRange(new AvailabilitySubmission { UserId = ids["employee"], SchedulePeriodId = periodId, LastSubmittedAt = DateTime.UtcNow, Version = 1 },
                new ShiftPreference { UserId = ids["employee"], ShiftId = shiftId, PreferenceType = PreferenceType.BLOCKED });
            await db.SaveChangesAsync();
        }
        var saved = await Send(client, $"{GenerationPath}/{run.Id}/save", new { }, "POST");
        Assert.Equal(HttpStatusCode.Conflict, saved.StatusCode);
        Assert.Equal("STALE_PREVIEW", (await saved.Content.ReadFromJsonAsync<ApiError>())!.Code);
    }

}
