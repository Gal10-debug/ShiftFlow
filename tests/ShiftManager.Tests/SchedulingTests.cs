using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using ShiftManager.Api;
using ShiftManager.Data;
using ShiftManager.Domain;
using ShiftManager.Services;

namespace ShiftManager.Tests;

public sealed partial class SchedulingTests : IAsyncLifetime
{
    private readonly TestDatabase database = new();
    private WebApplicationFactory<Program> factory = null!;
    private readonly Dictionary<string, Guid> ids = [];
    private const string Password = "SchedulingTest!2026";
    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-10-24T21:30:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private readonly TestClock clock = new();
    public async Task InitializeAsync()
    {
        await database.StartAsync();
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Development")
            .UseSetting("ConnectionStrings:Database", database.GetConnectionString())
            .ConfigureServices(s => { s.RemoveAll<TimeProvider>(); s.AddSingleton<TimeProvider>(clock); }));
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var (name, role) in new[] { ("admin", "ADMIN"), ("employee", "EMPLOYEE"), ("coordinator", "COORDINATOR"), ("firstlogin", "ADMIN") })
        {
            var user = new ApplicationUser { UserName = name, FirstName = "ישראל", LastName = name, IsActive = true, MustChangePassword = name == "firstlogin" };
            Assert.True((await users.CreateAsync(user, Password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, role)).Succeeded); ids[name] = user.Id;
        }
    }
    public async Task DisposeAsync() { if (factory is not null) await factory.DisposeAsync(); await database.DisposeAsync(); }
    private HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
    private static async Task<HttpResponseMessage> Send(HttpClient client, string path, object body, string method = "PUT", bool csrf = true)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(body) };
        if (csrf)
        {
            var token = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
            request.Headers.Add("X-CSRF-TOKEN", token.GetProperty("requestToken").GetString());
        }
        return await client.SendAsync(request);
    }
    private async Task<HttpClient> Login(string name = "admin")
    {
        var client = Client();
        Assert.Equal(HttpStatusCode.OK, (await Send(client, "/api/auth/login", new { username = name, password = Password }, "POST")).StatusCode);
        return client;
    }
    private const string TypesPath = "/api/admin/scheduling/shift-types";
    private static UpdateShiftTypesRequest Request(ShiftTypeResponse[] rows) => new(rows.Select(r => new ShiftTypeUpdate(r.Id, r.Version, r.Name, r.DisplayOrder, r.Period, r.RequiredEmployees, r.IncludeInAutomaticScheduling)).ToArray());
    private static async Task<ShiftTypeResponse[]> Types(HttpClient client) => (await client.GetFromJsonAsync<ShiftTypeResponse[]>(TypesPath))!;

    [PostgresFact]
    public async Task Exactly_51_seeded_rows_have_stable_identity_order_and_safe_defaults()
    {
        using var admin = await Login(); var rows = await Types(admin);
        Assert.Equal(51, rows.Length); Assert.Equal(51, rows.Select(x => x.Id).Distinct().Count());
        Assert.Equal(Enumerable.Range(1, 51), rows.Select(x => x.DisplayOrder));
        Assert.Equal(Enumerable.Range(1, 51), rows.Select(x => x.Slot));
        foreach (var row in rows)
        {
            Assert.Equal($"משמרת {row.Slot}", row.Name); Assert.Equal("DAY", row.Period);
            Assert.Equal(0, row.RequiredEmployees); Assert.False(row.IncludeInAutomaticScheduling);
        }
    }

    [PostgresFact]
    public async Task Rename_reorder_day_night_staffing_and_inclusion_persist_without_reseeding()
    {
        using var admin = await Login(); var rows = await Types(admin);
        var request = Request(rows);
        request.ShiftTypes[0] = request.ShiftTypes[0] with { Name = "צוות לילה", Period = "NIGHT", RequiredEmployees = 7, IncludeInAutomaticScheduling = true, DisplayOrder = 2 };
        request.ShiftTypes[1] = request.ShiftTypes[1] with { DisplayOrder = 1, RequiredEmployees = 0, IncludeInAutomaticScheduling = true };
        var result = await Send(admin, TypesPath, request);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var saved = (await result.Content.ReadFromJsonAsync<ShiftTypeResponse[]>())!;
        Assert.Equal(rows[1].Id, saved[0].Id); Assert.Equal(rows[0].Id, saved[1].Id);
        Assert.Equal("צוות לילה", saved[1].Name); Assert.Equal("NIGHT", saved[1].Period); Assert.Equal(7, saved[1].RequiredEmployees);
        Assert.Equal(2, saved[1].Version);
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(51, await db.Set<ShiftTypeDefinition>().CountAsync());
            Assert.Equal("צוות לילה", (await db.Set<ShiftTypeDefinition>().SingleAsync(x => x.Id == rows[0].Id)).Name);
        }
        // A fresh web host against the same database never resets names or settings.
        await using var restarted = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Development").UseSetting("ConnectionStrings:Database", database.GetConnectionString()));
        using var scope2 = restarted.Services.CreateScope();
        Assert.Equal("צוות לילה", (await scope2.ServiceProvider.GetRequiredService<SchedulingService>().List()).Single(x => x.Id == rows[0].Id).Name);
        var configuration = await scope2.ServiceProvider.GetRequiredService<IAutomaticSchedulingConfiguration>().GetConfiguration();
        Assert.Equal(rows[0].Id, Assert.Single(configuration.Rows).Id);
        Assert.Contains(rows[1].Id, configuration.ExcludedDefinitionIds); Assert.Equal(50, configuration.ExcludedDefinitionIds.Length);
    }

    [PostgresFact]
    public async Task Automatic_configuration_excludes_unselected_rows_even_when_staffing_is_positive()
    {
        using var admin = await Login(); var request = Request(await Types(admin));
        request.ShiftTypes[0] = request.ShiftTypes[0] with { RequiredEmployees = 3, IncludeInAutomaticScheduling = false };
        request.ShiftTypes[1] = request.ShiftTypes[1] with { RequiredEmployees = 2, IncludeInAutomaticScheduling = true };
        Assert.True((await Send(admin, TypesPath, request)).IsSuccessStatusCode);
        var configuration = (await admin.GetFromJsonAsync<AutomaticSchedulingConfigurationResponse>("/api/admin/scheduling/automatic-configuration"))!;
        Assert.Equal(request.ShiftTypes[1].Id, Assert.Single(configuration.Rows).Id);
        Assert.Contains(request.ShiftTypes[0].Id, configuration.ExcludedDefinitionIds);
        Assert.Equal("UNPUBLISHED", configuration.WorkspaceState);
    }

    [PostgresFact]
    public async Task Invalid_counts_names_periods_and_duplicate_orders_reject_the_whole_save()
    {
        using var admin = await Login(); var rows = await Types(admin);
        foreach (var invalid in new[] {
            Request(rows).ShiftTypes[0] with { RequiredEmployees = -1 }, Request(rows).ShiftTypes[0] with { RequiredEmployees = 1001 },
            Request(rows).ShiftTypes[0] with { Name = " " }, Request(rows).ShiftTypes[0] with { Name = new string('x', 101) },
            Request(rows).ShiftTypes[0] with { Period = "EVENING" }, Request(rows).ShiftTypes[0] with { DisplayOrder = 2 },
            Request(rows).ShiftTypes[0] with { RequiredEmployees = null }, Request(rows).ShiftTypes[0] with { IncludeInAutomaticScheduling = null } })
        {
            var request = Request(rows); request.ShiftTypes[0] = invalid;
            request.ShiftTypes[50] = request.ShiftTypes[50] with { Name = "אסור לשמור חלקית" };
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(admin, TypesPath, request)).StatusCode);
        }
        Assert.Equal("משמרת 51", (await Types(admin))[50].Name);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(admin, TypesPath, new UpdateShiftTypesRequest(Request(rows).ShiftTypes[..50]))).StatusCode);
        var duplicate = Request(rows); duplicate.ShiftTypes[1] = duplicate.ShiftTypes[0];
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(admin, TypesPath, duplicate)).StatusCode);
    }

    [PostgresFact]
    public async Task Concurrent_configuration_saves_reject_stale_versions_without_lost_updates()
    {
        using var one = await Login(); using var two = await Login(); var rows = await Types(one);
        var a = Request(rows); var b = Request(rows);
        a.ShiftTypes[0] = a.ShiftTypes[0] with { Name = "ראשון" }; b.ShiftTypes[0] = b.ShiftTypes[0] with { Name = "שני" };
        var results = await Task.WhenAll(Send(one, TypesPath, a), Send(two, TypesPath, b));
        Assert.Single(results, x => x.IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.Conflict, Assert.Single(results, x => !x.IsSuccessStatusCode).StatusCode);
    }

    [PostgresFact]
    public async Task Board_normalizes_to_sunday_spans_month_and_year_and_does_not_create_data()
    {
        using var admin = await Login();
        foreach (var (date, sunday, saturday) in new[] { ("2026-10-01", "2026-09-27", "2026-10-03"), ("2027-01-01", "2026-12-27", "2027-01-02") })
        {
            var board = (await admin.GetFromJsonAsync<WeeklyBoardResponse>($"/api/admin/scheduling/board?date={date}"))!;
            Assert.Equal(DateOnly.Parse(sunday), board.WeekStartsOn); Assert.Equal(DateOnly.Parse(saturday), board.WeekEndsOn);
            Assert.Equal(7, board.Days.Length); Assert.Equal(51, board.Rows.Length); Assert.Equal("UNPUBLISHED", board.WorkspaceState);
            Assert.All(board.Days, d => { Assert.Null(d.SchedulePeriodId); Assert.Equal(d.Date.Month, d.Month); Assert.Equal(d.Date.Year, d.Year); });
            Assert.All(board.Rows, r => { Assert.Equal(7, r.Cells.Length); Assert.All(r.Cells, c => { Assert.Null(c.ShiftId); Assert.Equal("NOT_REQUIRED", c.State); }); });
        }
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await db.Set<SchedulePeriod>().ToArrayAsync()); Assert.Empty(await db.Set<Shift>().ToArrayAsync());
    }

    [PostgresFact]
    public async Task Current_week_uses_Jerusalem_date_when_UTC_is_still_saturday()
    {
        using var admin = await Login();
        var board = (await admin.GetFromJsonAsync<WeeklyBoardResponse>("/api/admin/scheduling/board"))!;
        Assert.Equal(new DateOnly(2026, 10, 25), board.WeekStartsOn);
        Assert.Equal("Asia/Jerusalem", board.TimeZone);
        foreach (var date in new[] { "0001-01-01", "9999-12-31", "invalid" })
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync($"/api/admin/scheduling/board?date={date}")).StatusCode);
    }

    [PostgresFact]
    public async Task Night_cells_use_canonical_DST_boundaries_including_month_end()
    {
        using var admin = await Login(); var request = Request(await Types(admin));
        request.ShiftTypes[0] = request.ShiftTypes[0] with { Period = "NIGHT", RequiredEmployees = 1 };
        Assert.True((await Send(admin, TypesPath, request)).IsSuccessStatusCode);
        foreach (var (anchor, date, hours) in new[] { ("2026-10-24", "2026-10-24", 13), ("2026-03-26", "2026-03-26", 11), ("2026-10-31", "2026-10-31", 12) })
        {
            var board = (await admin.GetFromJsonAsync<WeeklyBoardResponse>($"/api/admin/scheduling/board?date={anchor}"))!;
            var cell = board.Rows.Single(x => x.Definition.Id == request.ShiftTypes[0].Id).Cells.Single(x => x.Date == DateOnly.Parse(date));
            Assert.Equal(hours, (cell.EndsAt - cell.StartsAt).TotalHours); Assert.Equal("UNASSIGNED", cell.State);
            var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Jerusalem");
            Assert.Equal(19, TimeZoneInfo.ConvertTimeFromUtc(cell.StartsAt, zone).Hour);
            Assert.Equal(cell.Date.AddDays(1), DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(cell.EndsAt, zone)));
        }
    }

    [PostgresFact]
    public async Task Only_administrators_can_read_or_update_and_CSRF_password_gate_and_deactivation_remain_enforced()
    {
        using var admin = await Login(); var request = Request(await Types(admin));
        using var anonymous = Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/admin/scheduling/board")).StatusCode);
        foreach (var role in new[] { "employee", "coordinator", "firstlogin" })
        {
            using var client = await Login(role);
            foreach (var path in new[] { TypesPath, "/api/admin/scheduling/board", "/api/admin/scheduling/automatic-configuration" })
                Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(client, TypesPath, request)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(admin, TypesPath, request, csrf: false)).StatusCode);
        using var stale = await Login();
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByIdAsync(ids["admin"].ToString()))!; user.IsActive = false;
            Assert.True((await users.UpdateAsync(user)).Succeeded);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await stale.GetAsync("/api/admin/scheduling/board")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(stale, TypesPath, request)).StatusCode);
    }

    [PostgresFact]
    public async Task Database_constraints_reject_invalid_staffing_period_order_slot_and_name()
    {
        await using var connection = new NpgsqlConnection(database.GetConnectionString()); await connection.OpenAsync();
        foreach (var set in new[] { "\"RequiredEmployees\" = -1", "\"RequiredEmployees\" = 1001", "\"Period\" = 'INVALID'", "\"DisplayOrder\" = 0", "\"Slot\" = 52", "\"Name\" = ' '" })
        {
            await using var tx = await connection.BeginTransactionAsync();
            var error = await Assert.ThrowsAsync<PostgresException>(() => new NpgsqlCommand($"UPDATE \"ShiftTypeDefinition\" SET {set} WHERE \"Slot\" = 1", connection, tx).ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState); await tx.RollbackAsync();
        }
        await using var duplicate = await connection.BeginTransactionAsync();
        await new NpgsqlCommand("UPDATE \"ShiftTypeDefinition\" SET \"DisplayOrder\" = 2 WHERE \"Slot\" = 1", connection, duplicate).ExecuteNonQueryAsync();
        var unique = await Assert.ThrowsAsync<PostgresException>(() => duplicate.CommitAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, unique.SqlState);
    }

    [PostgresFact]
    public async Task Additional_ranges_persist_and_drive_board_and_future_configuration()
    {
        using var admin = await Login(); var request = Request(await Types(admin));
        var ranges = new[] { "H06_09", "H07_13", "H07_15", "H15_23", "H23_07" };
        for (var i = 0; i < ranges.Length; i++)
            request.ShiftTypes[i] = request.ShiftTypes[i] with { TimeRange = ranges[i], Period = i == 4 ? "NIGHT" : "DAY", RequiredEmployees = 1, IncludeInAutomaticScheduling = true };
        Assert.True((await Send(admin, TypesPath, request)).IsSuccessStatusCode);
        var saved = await Types(admin);
        Assert.Equal(ranges, saved.Take(5).Select(x => x.TimeRange));
        var board = (await admin.GetFromJsonAsync<WeeklyBoardResponse>("/api/admin/scheduling/board?date=2026-11-01"))!;
        Assert.All(board.Rows.Take(5), row => { Assert.Null(row.Cells[0].ShiftId); Assert.NotEmpty(row.Cells[0].AvailabilityAnchors!); });
        Assert.Equal(new DateOnly(2026, 10, 31), board.Rows[0].Cells[0].AvailabilityAnchors![0].Date);
        Assert.Equal(2, board.Rows[3].Cells[0].AvailabilityAnchors!.Length);
        var configuration = (await admin.GetFromJsonAsync<AutomaticSchedulingConfigurationResponse>("/api/admin/scheduling/automatic-configuration"))!;
        Assert.Equal(ranges, configuration.Rows.Select(x => x.TimeRange));
        // Legacy clients changing names do not silently reset an existing custom range.
        var legacy = Request(saved); legacy.ShiftTypes[0] = legacy.ShiftTypes[0] with { Name = "עדכון שם בלבד" };
        Assert.True((await Send(admin, TypesPath, legacy)).IsSuccessStatusCode);
        Assert.Equal("H06_09", (await Types(admin))[0].TimeRange);
        var invalid = Request(await Types(admin)); invalid.ShiftTypes[0] = invalid.ShiftTypes[0] with { TimeRange = "INVALID" };
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(admin, TypesPath, invalid)).StatusCode);
    }

    [PostgresFact]
    public async Task Time_range_migration_preserves_existing_day_night_settings_and_names()
    {
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.GetService<IMigrator>().MigrateAsync("20261026063511_SchedulingBoardShiftTypes");
        await db.Database.ExecuteSqlRawAsync("UPDATE \"ShiftTypeDefinition\" SET \"Period\" = 'NIGHT', \"Name\" = 'שם קיים' WHERE \"Slot\" = 1");
        await db.Database.MigrateAsync(); db.ChangeTracker.Clear();
        var row = await db.Set<ShiftTypeDefinition>().SingleAsync(x => x.Slot == 1);
        Assert.Equal("שם קיים", row.Name); Assert.Equal(ShiftType.NIGHT, row.Period); Assert.Equal(ShiftTimeRange.NIGHT, row.TimeRange);
        Assert.Equal(51, await db.Set<ShiftTypeDefinition>().CountAsync());
    }

    [PostgresFact]
    public async Task Upgrade_from_phase_three_and_configuration_preserve_submissions_preferences_and_assignments()
    {
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.GetService<IMigrator>().MigrateAsync("20261025205611_AvailabilitySubmissions");
        var period = new SchedulePeriod { Year = 2026, Month = 10 };
        // Populate the historical schema using its columns, then attach the period unchanged.
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"SchedulePeriod\" (\"Id\",\"Year\",\"Month\",\"Status\",\"CreatedAt\",\"UpdatedAt\") VALUES ({period.Id},{period.Year},{period.Month},'DRAFT',{period.CreatedAt},{period.UpdatedAt})");
        db.Attach(period);
        var (start, end) = ShiftClock.Boundaries(new(2026, 10, 25), ShiftType.DAY);
        var shift = new Shift { SchedulePeriod = period, ShiftDate = new(2026, 10, 25), StartsAt = start, EndsAt = end };
        var preference = new ShiftPreference { UserId = ids["employee"], Shift = shift, PreferenceType = PreferenceType.PREFERRED };
        var submission = new AvailabilitySubmission { UserId = ids["employee"], SchedulePeriod = period, LastSubmittedAt = clock.Now.UtcDateTime, Version = 1 };
        var assignment = new ShiftAssignment { UserId = ids["employee"], Shift = shift, IsShiftLead = true };
        db.AddRange(preference, submission); await db.SaveChangesAsync();
        // Seed the historical schema without asking the current EF model to write new columns.
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"ShiftAssignment\" (\"Id\", \"ShiftId\", \"UserId\", \"IsShiftLead\", \"CreatedAt\") VALUES ({assignment.Id}, {shift.Id}, {assignment.UserId}, TRUE, {assignment.CreatedAt})");
        db.ChangeTracker.Clear();
        await db.Database.MigrateAsync(); Assert.False(db.Database.HasPendingModelChanges());
        using var admin = await Login(); var request = Request(await Types(admin));
        request.ShiftTypes[0] = request.ShiftTypes[0] with { Name = "שם מעודכן", Period = "NIGHT", RequiredEmployees = 9, IncludeInAutomaticScheduling = true };
        Assert.True((await Send(admin, TypesPath, request)).IsSuccessStatusCode);
        var board = (await admin.GetFromJsonAsync<WeeklyBoardResponse>("/api/admin/scheduling/board?date=2026-10-25"))!;
        Assert.Equal(period.Id, board.Days[0].SchedulePeriodId);
        Assert.Equal(shift.Id, board.Rows.Single(x => x.Definition.Slot == 2).Cells[0].ShiftId);
        db.ChangeTracker.Clear();
        Assert.Equal(4, await db.Users.CountAsync()); Assert.Equal(preference.Id, (await db.Set<ShiftPreference>().SingleAsync()).Id);
        Assert.Equal(submission.Id, (await db.Set<AvailabilitySubmission>().SingleAsync()).Id);
        Assert.Equal(assignment.Id, (await db.Set<ShiftAssignment>().SingleAsync()).Id);
        Assert.Equal(ShiftType.DAY, (await db.Set<Shift>().SingleAsync()).ShiftType);
        Assert.Equal(1, await db.Set<SchedulePeriod>().CountAsync());
    }
}
