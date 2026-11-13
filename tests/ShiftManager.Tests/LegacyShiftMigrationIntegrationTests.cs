using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ShiftManager.Data;
using ShiftManager.Domain;

namespace ShiftManager.Tests;

public sealed partial class SchedulingTests
{
    private static string CompatibilityPlan => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "local-migration-up.sql"));
    private const string OldDefinitions = """
        SELECT jsonb_agg(to_jsonb(d) ORDER BY "Slot")::text AS "Value" FROM "ShiftTypeDefinition" d
        """;
    private const string ProjectedDefinitions = """
        SELECT jsonb_agg(to_jsonb(d) - ARRAY['AssignmentMode','SchedulingPriority'] ORDER BY "Slot")::text AS "Value"
        FROM "ShiftTypeDefinition" d
        """;

    private static async Task CustomizeLegacy(ApplicationDbContext db)
    {
        await db.GetService<IMigrator>().MigrateAsync("20261027224058_AdditionalSchedulingConstraints");
        await db.Database.ExecuteSqlRawAsync("""
            UPDATE "ShiftTypeDefinition" SET "Semantic" = 'ORDINARY';
            UPDATE "ShiftTypeDefinition" SET
                "Name" = CASE "Slot" WHEN 1 THEN '6 עד 9' WHEN 12 THEN 'ב׳ בוקר' WHEN 13 THEN 'ב׳ לילה מומחה'
                    WHEN 14 THEN 'ב׳ לילה' WHEN 16 THEN 'שיקום ערב' WHEN 26 THEN 'כונן סופש' ELSE 'מותאם ' || "Slot" END,
                "Semantic" = CASE "Slot" WHEN 1 THEN 'SIX_NINE' WHEN 26 THEN 'WEEKEND_ON_CALL' ELSE 'ORDINARY' END,
                "RequiredEmployees" = 1, "IncludeInAutomaticScheduling" = ("Slot" % 5 <> 0),
                "DisplayOrder" = 52 - "Slot", "Version" = 7,
                "Period" = CASE WHEN "Slot" = 14 THEN 'NIGHT' ELSE 'DAY' END,
                "TimeRange" = CASE WHEN "Slot" = 14 THEN 'NIGHT' WHEN "Slot" = 16 THEN 'H15_23' ELSE 'DAY' END,
                "RequiresShiftLeadQualification" = ("Slot" % 2 = 1);
            """);
    }

    private async Task ApplyCompatibility(string plan)
    {
        await using var connection = new NpgsqlConnection(database.GetConnectionString());
        await connection.OpenAsync();
        try
        {
            await using var command = new NpgsqlCommand(plan, connection) { CommandTimeout = 300 };
            await command.ExecuteNonQueryAsync();
        }
        catch
        {
            await using var rollback = new NpgsqlCommand("ROLLBACK", connection);
            await rollback.ExecuteNonQueryAsync();
            throw;
        }
    }

    [PostgresFact]
    public async Task Legacy_compatibility_preserves_all_51_custom_definitions_and_admin_priority_control()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await CustomizeLegacy(db);
        var before = await db.Database.SqlQueryRaw<string>(OldDefinitions).SingleAsync();
        var users = await db.Users.CountAsync();
        await ApplyCompatibility(CompatibilityPlan);
        Assert.Equal(before, await db.Database.SqlQueryRaw<string>(ProjectedDefinitions).SingleAsync());
        Assert.Equal(users, await db.Users.CountAsync());
        Assert.False(db.Database.HasPendingModelChanges());
        var rows = await db.Set<ShiftTypeDefinition>().AsNoTracking().OrderBy(r => r.Slot).ToArrayAsync();
        Assert.Equal(51, rows.Length);
        Assert.All(rows, row => { Assert.Equal(AssignmentMode.Automatic, row.AssignmentMode); Assert.Equal(SchedulingPriority.Normal, row.SchedulingPriority); });
        Assert.Equal("ORDINARY", rows[15].Semantic); Assert.Equal("WEEKEND_ON_CALL", rows[25].Semantic);
        using var admin = await Login(); using var employee = await Login("employee");
        var configuration = await scope.ServiceProvider.GetRequiredService<Services.SchedulingService>().GetConfiguration();
        foreach (var slot in new[] { 12, 13, 14 })
            Assert.Contains(configuration.Rows, row => row.Id == rows[slot - 1].Id);
        var request = PriorityRequest(await Types(admin));
        var changes = new Api.UpdateShiftTypesRequest(request.ShiftTypes.Select(r => r with {
            SchedulingPriority = r.Id == rows[11].Id ? "Critical" : r.Id == rows[12].Id ? "High" : "Normal" }).ToArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(employee, TypesPath, changes)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(admin, TypesPath, changes)).StatusCode);
        await db.Database.MigrateAsync(); db.ChangeTracker.Clear();
        var saved = await db.Set<ShiftTypeDefinition>().AsNoTracking().OrderBy(r => r.Slot).ToArrayAsync();
        Assert.Equal(SchedulingPriority.Critical, saved[11].SchedulingPriority);
        Assert.Equal(SchedulingPriority.High, saved[12].SchedulingPriority);
        Assert.All(saved.Where(r => r.Slot is not (12 or 13)), r => Assert.Equal(SchedulingPriority.Normal, r.SchedulingPriority));
        var snapshot = await db.Database.SqlQueryRaw<string>(ProjectedDefinitions).SingleAsync();
        var rejected = await Assert.ThrowsAsync<PostgresException>(() => ApplyCompatibility(CompatibilityPlan));
        Assert.Equal(PostgresErrorCodes.RaiseException, rejected.SqlState);
        Assert.Equal(snapshot, await db.Database.SqlQueryRaw<string>(ProjectedDefinitions).SingleAsync());
        Assert.Equal(SchedulingPriority.Critical, (await db.Set<ShiftTypeDefinition>().AsNoTracking().SingleAsync(r => r.Slot == 12)).SchedulingPriority);
    }

    [PostgresFact]
    public async Task Legacy_compatibility_failure_rolls_back_schema_history_and_every_custom_shift_field()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await CustomizeLegacy(db);
        var before = await db.Database.SqlQueryRaw<string>(OldDefinitions).SingleAsync();
        var broken = CompatibilityPlan.Replace("ALTER TABLE \"ShiftTypeDefinition\" ADD \"SchedulingPriority\"",
            "SELECT 1 / 0;\nALTER TABLE \"ShiftTypeDefinition\" ADD \"SchedulingPriority\"");
        Assert.NotEqual(CompatibilityPlan, broken);
        var error = await Assert.ThrowsAsync<PostgresException>(() => ApplyCompatibility(broken));
        Assert.Equal(PostgresErrorCodes.DivisionByZero, error.SqlState);
        Assert.Equal(before, await db.Database.SqlQueryRaw<string>(OldDefinitions).SingleAsync());
        Assert.Equal(12, (await db.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Equal(5, (await db.Database.GetPendingMigrationsAsync()).Count());
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM information_schema.columns
            WHERE table_schema='public' AND table_name='ShiftTypeDefinition' AND column_name IN ('AssignmentMode','SchedulingPriority')
            """).SingleAsync());
    }
}
