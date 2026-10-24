using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShiftManager.Data;
using ShiftManager.Domain;
using Testcontainers.PostgreSql;

namespace ShiftManager.Tests;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("RUN_POSTGRES_TESTS") != "1")
            Skip = "Set RUN_POSTGRES_TESTS=1 to run real PostgreSQL integration tests (Docker required).";
    }
}

public class PostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer database = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private ApplicationDbContext Context() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql(database.GetConnectionString()).Options);

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        await using var db = Context();
        await db.Database.MigrateAsync();
    }
    public async Task DisposeAsync() => await database.DisposeAsync();

    [PostgresFact]
    public async Task Migration_applies_and_seeds_only_expected_roles()
    {
        await using var db = Context();
        Assert.NotEmpty(await db.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(new[] { "ADMIN", "COORDINATOR", "EMPLOYEE" }, await db.Roles.OrderBy(x => x.Name).Select(x => x.Name).ToArrayAsync());
        Assert.Empty(await db.Users.ToListAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [PostgresFact]
    public async Task Hebrew_content_and_relationships_round_trip_and_history_cannot_cascade()
    {
        await using var db = Context();
        var period = new SchedulePeriod { Year = 2026, Month = 1 };
        var user = new ApplicationUser { Id = Guid.NewGuid(), FirstName = "ישראל", LastName = "ישראלי" };
        var (start, end) = ShiftClock.Boundaries(new DateOnly(2026, 1, 1), ShiftType.DAY);
        var shift = new Shift { SchedulePeriod = period, ShiftDate = new(2026, 1, 1), StartsAt = start, EndsAt = end };
        var assignment = new ShiftAssignment { Shift = shift, User = user, IsShiftLead = true };
        var task = new ShiftTask { Shift = shift, Title = "בדיקת ציוד", Description = "משימה יומית", CreatedByUser = user };
        db.AddRange(assignment, new TaskAssignment { ShiftTask = task, ShiftAssignment = assignment, StartsAt = start, EndsAt = end });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var saved = await db.Set<TaskAssignment>().Include(x => x.ShiftTask).Include(x => x.ShiftAssignment).ThenInclude(x => x.User).SingleAsync();
        Assert.Equal("בדיקת ציוד", saved.ShiftTask.Title);
        Assert.Equal("ישראל", saved.ShiftAssignment.User.FirstName);
        await using var connection = new NpgsqlConnection(database.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("DELETE FROM \"SchedulePeriod\"", connection);
        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
    }

    [PostgresFact]
    public async Task PostgreSql_rejects_invalid_data_with_expected_sqlstates()
    {
        await using var connection = new NpgsqlConnection(database.GetConnectionString());
        await connection.OpenAsync();
        var id = Guid.NewGuid();
        var now = "'2026-01-01T00:00:00Z'";
        var period = $"INSERT INTO \"SchedulePeriod\" (\"Id\",\"Year\",\"Month\",\"Status\",\"CreatedAt\",\"UpdatedAt\") VALUES ('{id}',2026,1,'DRAFT',{now},{now})";
        async Task Reject(string seed, string sql, string code)
        {
            await using var tx = await connection.BeginTransactionAsync();
            if (seed.Length > 0) await new NpgsqlCommand(seed, connection, tx).ExecuteNonQueryAsync();
            var error = await Assert.ThrowsAsync<PostgresException>(() => new NpgsqlCommand(sql, connection, tx).ExecuteNonQueryAsync());
            Assert.Equal(code, error.SqlState);
            await tx.RollbackAsync();
        }
        await Reject(period, period.Replace(id.ToString(), Guid.NewGuid().ToString()), PostgresErrorCodes.UniqueViolation);
        await Reject("", period.Replace(",1,'DRAFT'", ",13,'DRAFT'"), PostgresErrorCodes.CheckViolation);
        await Reject("", period.Replace("'DRAFT'", "'INVALID'"), PostgresErrorCodes.CheckViolation);
        await Reject("", period.Replace("'DRAFT'", "NULL"), PostgresErrorCodes.NotNullViolation);
        await Reject("", $"INSERT INTO \"Shift\" (\"Id\",\"SchedulePeriodId\",\"ShiftDate\",\"ShiftType\",\"StartsAt\",\"EndsAt\") VALUES ('{Guid.NewGuid()}','{id}','2026-01-01','DAY',{now},'2026-01-01T12:00:00Z')", PostgresErrorCodes.ForeignKeyViolation);
        await Reject(period, $"INSERT INTO \"Shift\" (\"Id\",\"SchedulePeriodId\",\"ShiftDate\",\"ShiftType\",\"StartsAt\",\"EndsAt\") VALUES ('{Guid.NewGuid()}','{id}','2026-01-01','DAY',{now},{now})", PostgresErrorCodes.CheckViolation);
    }

    [PostgresFact]
    public async Task Filtered_lead_index_allows_multiple_members_but_rejects_second_lead()
    {
        await using var db = Context();
        var period = new SchedulePeriod { Year = 2026, Month = 2 };
        var shift = new Shift { SchedulePeriod = period, ShiftDate = new(2026, 2, 1), StartsAt = DateTime.UtcNow, EndsAt = DateTime.UtcNow.AddHours(12) };
        var users = Enumerable.Range(0, 3).Select(_ => new ApplicationUser { Id = Guid.NewGuid(), FirstName = "שם", LastName = "משפחה" }).ToArray();
        db.AddRange(users.Select((user, i) => new ShiftAssignment { Shift = shift, User = user, IsShiftLead = i == 0 }));
        await db.SaveChangesAsync();
        var member = await db.Set<ShiftAssignment>().SingleAsync(x => x.UserId == users[1].Id);
        member.IsShiftLead = true;
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    [PostgresFact]
    public async Task All_business_unique_indexes_reject_duplicates()
    {
        await using var db = Context();
        var user = new ApplicationUser { Id = Guid.NewGuid(), FirstName = "שם", LastName = "משפחה" };
        var period = new SchedulePeriod { Year = 2026, Month = 4 };
        var start = new DateTime(2026, 4, 1, 4, 0, 0, DateTimeKind.Utc);
        var shift = new Shift { SchedulePeriod = period, ShiftDate = new(2026, 4, 1), StartsAt = start, EndsAt = start.AddHours(12) };
        db.AddRange(new AvailabilityWindow { SchedulePeriod = period, OpenedByUser = user, OpensAt = start },
            new ShiftPreference { Shift = shift, User = user },
            new ShiftAssignment { Shift = shift, User = user },
            new SchedulePublication { SchedulePeriod = period, PublishedByUser = user, VersionNumber = 1, PublishedAt = start });
        await db.SaveChangesAsync();
        object[] duplicates = [
            new SchedulePeriod { Year = 2026, Month = 4 },
            new AvailabilityWindow { SchedulePeriodId = period.Id, OpenedByUserId = user.Id, OpensAt = start },
            new Shift { SchedulePeriodId = period.Id, ShiftDate = shift.ShiftDate, StartsAt = start, EndsAt = start.AddHours(12) },
            new ShiftPreference { ShiftId = shift.Id, UserId = user.Id },
            new ShiftAssignment { ShiftId = shift.Id, UserId = user.Id },
            new SchedulePublication { SchedulePeriodId = period.Id, PublishedByUserId = user.Id, VersionNumber = 1, PublishedAt = start }
        ];
        foreach (var duplicate in duplicates)
        {
            await using var attempt = Context();
            attempt.Add(duplicate);
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => attempt.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        }
    }

    [PostgresFact]
    public async Task Tasks_require_same_shift_and_contained_times_but_can_overlap()
    {
        await using var db = Context();
        var user = new ApplicationUser { Id = Guid.NewGuid(), FirstName = "שם", LastName = "משפחה" };
        var period = new SchedulePeriod { Year = 2026, Month = 5 };
        var start = new DateTime(2026, 5, 1, 4, 0, 0, DateTimeKind.Utc);
        var shift = new Shift { SchedulePeriod = period, ShiftDate = new(2026, 5, 1), StartsAt = start, EndsAt = start.AddHours(12) };
        var other = new Shift { SchedulePeriod = period, ShiftDate = new(2026, 5, 2), StartsAt = start.AddDays(1), EndsAt = start.AddDays(1).AddHours(12) };
        var member = new ShiftAssignment { Shift = shift, User = user };
        var otherMember = new ShiftAssignment { Shift = other, User = user };
        var task = new ShiftTask { Shift = shift, CreatedByUser = user, Title = "משימה" };
        db.AddRange(member, otherMember, task);
        await db.SaveChangesAsync();
        async Task Reject(TaskAssignment assignment)
        {
            await using var attempt = Context();
            attempt.Add(assignment);
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => attempt.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        }
        await Reject(new TaskAssignment { ShiftTaskId = task.Id, ShiftAssignmentId = otherMember.Id, StartsAt = start, EndsAt = start.AddHours(1) });
        await Reject(new TaskAssignment { ShiftTaskId = task.Id, ShiftAssignmentId = member.Id, StartsAt = start.AddMinutes(-1), EndsAt = start.AddHours(1) });
        await Reject(new TaskAssignment { ShiftTaskId = task.Id, ShiftAssignmentId = member.Id, StartsAt = start, EndsAt = start.AddHours(13) });
        await Reject(new TaskAssignment { ShiftTaskId = task.Id, ShiftAssignmentId = member.Id, StartsAt = start, EndsAt = start });
        db.AddRange(new TaskAssignment { ShiftTaskId = task.Id, ShiftAssignmentId = member.Id, StartsAt = start, EndsAt = start.AddHours(2) },
            new TaskAssignment { ShiftTaskId = task.Id, ShiftAssignmentId = member.Id, StartsAt = start, EndsAt = start.AddHours(3) });
        await db.SaveChangesAsync();
        Assert.Equal(2, await db.Set<TaskAssignment>().CountAsync());
        // Parent edits cannot invalidate already stored task assignments.
        foreach (var parent in new[] { "shift", "task", "member" })
        {
            await using var attempt = Context();
            if (parent == "shift") (await attempt.Set<Shift>().SingleAsync(x => x.Id == shift.Id)).StartsAt = start.AddHours(1);
            if (parent == "task") (await attempt.Set<ShiftTask>().SingleAsync(x => x.Id == task.Id)).ShiftId = other.Id;
            if (parent == "member") (await attempt.Set<ShiftAssignment>().SingleAsync(x => x.Id == member.Id)).ShiftId = other.Id;
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => attempt.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        }
    }

    [PostgresFact]
    public async Task Migration_can_roll_back_and_reapply_cleanly()
    {
        await using var db = Context();
        var migrator = Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>(db);
        await migrator.MigrateAsync("0");
        Assert.Empty(await db.Database.GetAppliedMigrationsAsync());
        await db.Database.MigrateAsync();
        Assert.Equal(3, await db.Roles.CountAsync());
    }

    [PostgresFact]
    public async Task Required_fields_and_remaining_check_constraints_are_enforced()
    {
        await using var db = Context();
        var user = new ApplicationUser { Id = Guid.NewGuid(), FirstName = "שם", LastName = "משפחה" };
        var period = new SchedulePeriod { Year = 2026, Month = 6 };
        var start = new DateTime(2026, 6, 1, 4, 0, 0, DateTimeKind.Utc);
        var shift = new Shift { SchedulePeriod = period, ShiftDate = new(2026, 6, 1), StartsAt = start, EndsAt = start.AddHours(12) };
        db.AddRange(user, shift);
        await db.SaveChangesAsync();
        (object Entity, string Code)[] invalid = [
            (new TaskTemplate { Name = null! }, PostgresErrorCodes.NotNullViolation),
            (new TaskTemplate { Name = " " }, PostgresErrorCodes.CheckViolation),
            (new ApplicationUser { Id = Guid.NewGuid(), FirstName = "", LastName = "שם" }, PostgresErrorCodes.CheckViolation),
            (new SchedulePeriod { Year = 0, Month = 1 }, PostgresErrorCodes.CheckViolation),
            (new AvailabilityWindow { SchedulePeriodId = period.Id, OpenedByUserId = user.Id, OpensAt = start, ClosesAt = start.AddMinutes(-1) }, PostgresErrorCodes.CheckViolation),
            (new AvailabilityWindow { SchedulePeriodId = period.Id, OpenedByUserId = user.Id, OpensAt = start, Status = (WindowStatus)999 }, PostgresErrorCodes.CheckViolation),
            (new Shift { SchedulePeriodId = period.Id, ShiftDate = shift.ShiftDate, StartsAt = start, EndsAt = start.AddHours(1), ShiftType = (ShiftType)999 }, PostgresErrorCodes.CheckViolation),
            (new ShiftPreference { ShiftId = shift.Id, UserId = user.Id, PreferenceType = (PreferenceType)999 }, PostgresErrorCodes.CheckViolation),
            (new SchedulePublication { SchedulePeriodId = period.Id, PublishedByUserId = user.Id, VersionNumber = 0 }, PostgresErrorCodes.CheckViolation),
            (new ShiftTask { ShiftId = shift.Id, CreatedByUserId = user.Id, Title = " " }, PostgresErrorCodes.CheckViolation),
            (new ShiftTask { ShiftId = shift.Id, CreatedByUserId = user.Id, Title = null! }, PostgresErrorCodes.NotNullViolation)
        ];
        foreach (var (entity, code) in invalid)
        {
            await using var attempt = Context();
            attempt.Add(entity);
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => attempt.SaveChangesAsync());
            Assert.Equal(code, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        }
    }
}
