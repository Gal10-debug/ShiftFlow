using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShiftManager.Domain;

namespace ShiftManager.Tests;

public partial class PostgresTests
{
    [PostgresFact]
    public async Task Employee_type_migration_preserves_existing_users_and_schedules_and_defaults_to_regular()
    {
        await using var db = Context();
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "legacy", FirstName = "שם", LastName = "קיים" };
        var period = new SchedulePeriod { Year = 2026, Month = 10 };
        var date = new DateOnly(2026, 10, 1); var time = ShiftClock.Boundaries(date, ShiftType.DAY);
        var assignment = new ShiftAssignment { User = user, Shift = new Shift { SchedulePeriod = period, ShiftDate = date, StartsAt = time.StartsAt, EndsAt = time.EndsAt } };
        db.Add(assignment); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await db.GetService<IMigrator>().MigrateAsync("20261106074419_SchedulingPriority");
        var before = await db.Database.SqlQueryRaw<string>("""
            SELECT (to_jsonb(u))::text AS "Value" FROM "AspNetUsers" u
            """).SingleAsync();
        await db.Database.MigrateAsync();
        Assert.Equal(before, await db.Database.SqlQueryRaw<string>("""
            SELECT (to_jsonb(u) - 'EmployeeType')::text AS "Value" FROM "AspNetUsers" u
            """).SingleAsync());
        Assert.Equal(EmployeeType.REGULAR, (await db.Users.SingleAsync()).EmployeeType);
        Assert.Equal(assignment.Id, (await db.Set<ShiftAssignment>().SingleAsync()).Id);
        Assert.Equal(period.Id, (await db.Set<SchedulePeriod>().SingleAsync()).Id);
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
