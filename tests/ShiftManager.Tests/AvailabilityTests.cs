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

public sealed class AvailabilityTests : IAsyncLifetime
{
    private readonly TestDatabase database = new();
    private readonly TestClock clock = new();
    private WebApplicationFactory<Program> factory = null!;
    private readonly Dictionary<string, Guid> ids = [];
    private const string Password = "AvailabilityTest!2026";
    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-10-20T12:00:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }
    public async Task InitializeAsync()
    {
        await database.StartAsync();
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Development")
            .UseSetting("ConnectionStrings:Database", database.GetConnectionString())
            .ConfigureServices(s => { s.RemoveAll<TimeProvider>(); s.AddSingleton<TimeProvider>(clock); }));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var (name, role) in new[] { ("admin", "ADMIN"), ("employee", "EMPLOYEE"), ("other", "EMPLOYEE"), ("coordinator", "COORDINATOR"), ("firstlogin", "EMPLOYEE"), ("inactive", "EMPLOYEE") })
        {
            var user = new ApplicationUser { UserName = name, FirstName = "דנה", LastName = name, IsActive = name != "inactive", MustChangePassword = name == "firstlogin" };
            Assert.True((await users.CreateAsync(user, Password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, role)).Succeeded); ids[name] = user.Id;
        }
    }
    public async Task DisposeAsync() { if (factory is not null) await factory.DisposeAsync(); await database.DisposeAsync(); }
    private HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
    private async Task<HttpClient> Login(string name = "employee")
    {
        var client = Client();
        Assert.Equal(HttpStatusCode.OK, (await Send(client, "/api/auth/login", new { username = name, password = Password }, "POST")).StatusCode);
        return client;
    }
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
    private static async Task Error(HttpResponseMessage result, string code, HttpStatusCode status = HttpStatusCode.BadRequest)
    {
        Assert.Equal(status, result.StatusCode);
        Assert.Equal(code, (await result.Content.ReadFromJsonAsync<ApiError>())!.Code);
    }
    private async Task Open(int year = 2026, int month = 10)
    {
        using var admin = await Login("admin");
        var configured = await Send(admin, $"/api/admin/availability/{year}/{month}", new WindowRequest(clock.Now.AddDays(-1), clock.Now.AddDays(60)));
        Assert.Equal(HttpStatusCode.OK, configured.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(admin, $"/api/admin/availability/{year}/{month}/state", new { isOpen = true })).StatusCode);
    }
    private static SelectionRequest Pick(string date, string type = "DAY", string state = "BLOCKED") => new(DateOnly.Parse(date), type, state);
    private static SubmitAvailabilityRequest Input(long version = 0, params SelectionRequest[] rows) => new(version, rows);

    [PostgresFact]
    public async Task All_available_is_durable_submission_and_explicit_updates_replace_full_month()
    {
        await Open(); using var employee = await Login();
        Assert.False((await employee.GetFromJsonAsync<AvailabilityResponse>("/api/availability/2026/10"))!.Submitted);
        var first = await Send(employee, "/api/availability/2026/10", Input(), "POST");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var submitted = (await first.Content.ReadFromJsonAsync<AvailabilityResponse>())!;
        Assert.True(submitted.Submitted); Assert.NotNull(submitted.LastSubmittedAt); Assert.Equal(1, submitted.Version); Assert.Empty(submitted.Selections);
        clock.Now = clock.Now.AddMinutes(2);
        Assert.Equal(HttpStatusCode.OK, (await Send(employee, "/api/availability/2026/10", Input(1, Pick("2026-10-25"), Pick("2026-10-26", state: "PREFERRED")))).StatusCode);
        var current = (await employee.GetFromJsonAsync<AvailabilityResponse>("/api/availability/2026/10"))!;
        Assert.Equal(2, current.Version); Assert.Equal(2, current.Selections.Length); Assert.True(current.LastSubmittedAt > submitted.LastSubmittedAt);
        Assert.Equal(HttpStatusCode.OK, (await Send(employee, "/api/availability/2026/10", Input(2))).StatusCode);
        current = (await employee.GetFromJsonAsync<AvailabilityResponse>("/api/availability/2026/10"))!;
        Assert.True(current.Submitted); Assert.Empty(current.Selections);
        using var scope = factory.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<IHardBlockQuery>().BlockedShifts(ids["employee"], new(2026, 10, 1), new(2026, 10, 31)));
    }

    [PostgresFact]
    public async Task Weekly_counts_zero_through_three_are_allowed_four_is_atomic_failure()
    {
        await Open(); using var employee = await Login();
        for (var count = 0; count <= 3; count++)
        {
            var rows = Enumerable.Range(0, count).Select(i => Pick($"2026-10-{25 + i}")).ToArray();
            var result = await Send(employee, "/api/availability/2026/10", Input(count, rows), count == 0 ? "POST" : "PUT");
            Assert.True(result.IsSuccessStatusCode);
        }
        var failure = await Send(employee, "/api/availability/2026/10", Input(4, Enumerable.Range(25, 4).Select(d => Pick($"2026-10-{d}")).ToArray()));
        Assert.Equal(HttpStatusCode.BadRequest, failure.StatusCode);
        var error = (await failure.Content.ReadFromJsonAsync<ApiError>())!;
        Assert.Equal("WEEKLY_BLOCK_LIMIT", error.Code);
        Assert.Equal(new DateOnly(2026, 10, 25), error.WeekStartsOn); Assert.Equal(3, error.Limit);
        var saved = (await employee.GetFromJsonAsync<AvailabilityResponse>("/api/availability/2026/10"))!;
        Assert.Equal(4, saved.Version); Assert.Equal(3, saved.Selections.Length);
    }

    [PostgresFact]
    public async Task Cross_month_replacement_counts_other_month_and_excludes_own_old_blocks()
    {
        await Open(2026, 9); await Open(); using var employee = await Login();
        Assert.True((await Send(employee, "/api/availability/2026/9", Input(0, Pick("2026-09-30"), Pick("2026-09-30", "NIGHT")), "POST")).IsSuccessStatusCode);
        Assert.True((await Send(employee, "/api/availability/2026/10", Input(0, Pick("2026-10-01")), "POST")).IsSuccessStatusCode);
        Assert.True((await Send(employee, "/api/availability/2026/10", Input(1, Pick("2026-10-02")))).IsSuccessStatusCode);
        await Error(await Send(employee, "/api/availability/2026/10", Input(2, Pick("2026-10-02"), Pick("2026-10-03"))), "WEEKLY_BLOCK_LIMIT");
        var current = (await employee.GetFromJsonAsync<AvailabilityResponse>("/api/availability/2026/10"))!;
        Assert.Equal("2026-10-02", current.Selections.Single().Date.ToString("yyyy-MM-dd"));
        Assert.Equal(2, current.Weeks.Single(x => x.StartsOn == new DateOnly(2026, 9, 27)).OtherMonthBlocks);
    }

    [PostgresFact]
    public async Task Simultaneous_adjacent_month_submissions_cannot_exceed_weekly_limit()
    {
        await Open(2026, 9); await Open(); using var first = await Login(); using var second = await Login();
        var results = await Task.WhenAll(
            Send(first, "/api/availability/2026/9", Input(0, Pick("2026-09-30"), Pick("2026-09-30", "NIGHT")), "POST"),
            Send(second, "/api/availability/2026/10", Input(0, Pick("2026-10-01"), Pick("2026-10-01", "NIGHT")), "POST"));
        Assert.Single(results, r => r.IsSuccessStatusCode);
        await Error(Assert.Single(results, r => !r.IsSuccessStatusCode), "WEEKLY_BLOCK_LIMIT");
        using var scope = factory.Services.CreateScope();
        Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Set<ShiftPreference>().CountAsync());
    }

    [PostgresFact]
    public async Task Concurrent_same_month_updates_have_one_winner_and_stale_versions_conflict()
    {
        await Open(); using var one = await Login(); using var two = await Login();
        Assert.True((await Send(one, "/api/availability/2026/10", Input(), "POST")).IsSuccessStatusCode);
        var results = await Task.WhenAll(Send(one, "/api/availability/2026/10", Input(1, Pick("2026-10-25"))), Send(two, "/api/availability/2026/10", Input(1, Pick("2026-10-26"))));
        Assert.Single(results, r => r.IsSuccessStatusCode);
        await Error(Assert.Single(results, r => !r.IsSuccessStatusCode), "CONFLICT", HttpStatusCode.Conflict);
        await Error(await Send(one, "/api/availability/2026/10", Input(), "POST"), "CONFLICT", HttpStatusCode.Conflict);
    }

    [PostgresFact]
    public async Task Time_range_manual_closing_and_reopening_are_enforced_without_losing_submission()
    {
        await Open(); using var employee = await Login(); using var admin = await Login("admin");
        var path = "/api/admin/availability/2026/10";
        Assert.True((await Send(admin, path, new WindowRequest(clock.Now.AddHours(1), clock.Now.AddHours(2)))).IsSuccessStatusCode);
        Assert.Equal("SCHEDULED", (await employee.GetFromJsonAsync<AvailabilityResponse>("/api/availability/2026/10"))!.Window.Status);
        await Error(await Send(employee, "/api/availability/2026/10", Input(), "POST"), "WINDOW_CLOSED", HttpStatusCode.Conflict);
        clock.Now = clock.Now.AddHours(1);
        Assert.True((await Send(employee, "/api/availability/2026/10", Input(), "POST")).IsSuccessStatusCode);
        clock.Now = clock.Now.AddHours(1); // Exclusive deadline.
        await Error(await Send(employee, "/api/availability/2026/10", Input(1)), "WINDOW_CLOSED", HttpStatusCode.Conflict);
        Assert.True((await Send(admin, path + "/state", new { isOpen = false })).IsSuccessStatusCode);
        Assert.True((await Send(admin, path, new WindowRequest(clock.Now.AddDays(-1), clock.Now.AddDays(1)))).IsSuccessStatusCode);
        await Error(await Send(employee, "/api/availability/2026/10", Input(1)), "WINDOW_CLOSED", HttpStatusCode.Conflict);
        Assert.True((await Send(admin, path + "/state", new { isOpen = true })).IsSuccessStatusCode);
        Assert.True((await Send(employee, "/api/availability/2026/10", Input(1))).IsSuccessStatusCode);
        Assert.True((await Send(admin, path + "/state", new { isOpen = false })).IsSuccessStatusCode);
        await Error(await Send(employee, "/api/availability/2026/10", Input(2)), "WINDOW_CLOSED", HttpStatusCode.Conflict);
        Assert.True((await employee.GetFromJsonAsync<AvailabilityResponse>("/api/availability/2026/10"))!.Submitted);
    }

    [PostgresFact]
    public async Task Admin_summary_distinguishes_all_available_missing_and_ineligible_employees()
    {
        await Open(); using var employee = await Login(); using var admin = await Login("admin");
        Assert.True((await Send(employee, "/api/availability/2026/10", Input(), "POST")).IsSuccessStatusCode);
        var result = (await admin.GetFromJsonAsync<WindowSummaryResponse>("/api/admin/availability/2026/10"))!;
        Assert.Equal(3, result.EligibleCount); Assert.Equal(1, result.SubmittedCount);
        Assert.True(result.Employees.Single(x => x.UserId == ids["employee"]).Submitted);
        Assert.False(result.Employees.Single(x => x.UserId == ids["other"]).Submitted);
        var detail = (await admin.GetFromJsonAsync<AvailabilityResponse>($"/api/admin/availability/2026/10/employees/{ids["employee"]}"))!;
        Assert.True(detail.Submitted); Assert.Empty(detail.Selections);
        var periods = await employee.GetFromJsonAsync<PeriodResponse[]>("/api/availability/periods");
        Assert.True(Assert.Single(periods!).Submitted);
    }

    [PostgresFact]
    public async Task Roles_csrf_and_identity_are_enforced_and_admin_has_no_submission_override()
    {
        await Open(); using var employee = await Login(); using var coordinator = await Login("coordinator"); using var admin = await Login("admin"); using var first = await Login("firstlogin"); using var anonymous = Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/availability/2026/10")).StatusCode);
        foreach (var client in new[] { employee, coordinator })
            await Error(await Send(client, "/api/admin/availability/2026/10/state", new { isOpen = false }), "FORBIDDEN", HttpStatusCode.Forbidden);
        await Error(await coordinator.GetAsync("/api/availability/2026/10"), "FORBIDDEN", HttpStatusCode.Forbidden);
        await Error(await first.GetAsync("/api/availability/2026/10"), "PASSWORD_CHANGE_REQUIRED", HttpStatusCode.Forbidden);
        await Error(await Send(admin, "/api/availability/2026/10", Input(), "POST"), "FORBIDDEN", HttpStatusCode.Forbidden);
        await Error(await employee.GetAsync($"/api/admin/availability/2026/10/employees/{ids["other"]}"), "FORBIDDEN", HttpStatusCode.Forbidden);
        await Error(await Send(employee, "/api/availability/2026/10", new { expectedVersion = 0, selections = Array.Empty<object>(), userId = ids["other"] }, "POST"), "VALIDATION_ERROR");
        await Error(await Send(employee, "/api/availability/2026/10", Input(), "POST", csrf: false), "CSRF_INVALID");
        await Error(await Send(admin, "/api/admin/availability/2026/10/state", new { isOpen = false }, csrf: false), "CSRF_INVALID");
        Assert.Equal(HttpStatusCode.NotFound, (await Send(admin, $"/api/admin/availability/2026/10/employees/{ids["employee"]}", Input())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await employee.GetAsync($"/api/availability/2026/10/employees/{ids["other"]}")).StatusCode);
    }

    [PostgresFact]
    public async Task Deactivation_revokes_availability_access_and_preserves_history()
    {
        await Open(); using var employee = await Login(); using var admin = await Login("admin");
        Assert.True((await Send(employee, "/api/availability/2026/10", Input(), "POST")).IsSuccessStatusCode);
        Assert.True((await Send(admin, $"/api/users/{ids["employee"]}/status", new { isActive = false }, "PATCH")).IsSuccessStatusCode);
        await Error(await Send(employee, "/api/availability/2026/10", Input(1)), "UNAUTHENTICATED", HttpStatusCode.Unauthorized);
        Assert.True((await admin.GetFromJsonAsync<AvailabilityResponse>($"/api/admin/availability/2026/10/employees/{ids["employee"]}"))!.Submitted);
        Assert.DoesNotContain((await admin.GetFromJsonAsync<WindowSummaryResponse>("/api/admin/availability/2026/10"))!.Employees, x => x.UserId == ids["employee"]);
    }

    [PostgresFact]
    public async Task Invalid_dates_duplicates_enum_values_and_window_times_are_rejected()
    {
        await Open(); using var employee = await Login(); using var admin = await Login("admin");
        foreach (var rows in new[] { new[] { Pick("2026-09-30") }, new[] { Pick("2026-10-01"), Pick("2026-10-01", state: "PREFERRED") }, new[] { Pick("2026-10-01", "INVALID") }, new[] { Pick("2026-10-01", state: "INVALID") } })
            await Error(await Send(employee, "/api/availability/2026/10", Input(0, rows), "POST"), "VALIDATION_ERROR");
        await Error(await Send(admin, "/api/admin/availability/2026/10", new WindowRequest(clock.Now, clock.Now)), "INVALID_WINDOW_TIME");
        await Error(await Send(admin, "/api/admin/availability/2026/10", new { opensAt = clock.Now }), "VALIDATION_ERROR");
        await Error(await employee.GetAsync("/api/availability/2026/13"), "VALIDATION_ERROR");
        await Error(await Send(employee, "/api/availability/2026/10", Input(1)), "CONFLICT", HttpStatusCode.Conflict);
    }

    [PostgresFact]
    public async Task Duplicate_window_configuration_is_safe_and_shifts_have_canonical_dst_boundaries()
    {
        using var one = await Login("admin"); using var two = await Login("admin");
        var results = await Task.WhenAll(Send(one, "/api/admin/availability/2026/10", new WindowRequest(clock.Now, clock.Now.AddDays(1))), Send(two, "/api/admin/availability/2026/10", new WindowRequest(clock.Now, clock.Now.AddDays(2))));
        Assert.All(results, x => Assert.True(x.IsSuccessStatusCode));
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Single(await db.Set<AvailabilityWindow>().ToArrayAsync()); Assert.Equal(62, await db.Set<Shift>().CountAsync());
        var night = await db.Set<Shift>().SingleAsync(x => x.ShiftDate == new DateOnly(2026, 10, 24) && x.ShiftType == ShiftType.NIGHT);
        Assert.Equal(13, (night.EndsAt - night.StartsAt).TotalHours);
    }

    [PostgresFact]
    public async Task Hard_block_query_and_database_backstop_prevent_even_direct_admin_assignment()
    {
        await Open(); using var employee = await Login();
        Assert.True((await Send(employee, "/api/availability/2026/10", Input(0, Pick("2026-10-25")), "POST")).IsSuccessStatusCode);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var shift = await db.Set<Shift>().SingleAsync(x => x.ShiftDate == new DateOnly(2026, 10, 25) && x.ShiftType == ShiftType.DAY);
        var query = scope.ServiceProvider.GetRequiredService<IHardBlockQuery>(); Assert.True(await query.IsBlocked(ids["employee"], shift.Id));
        Assert.Contains(shift.Id, await query.BlockedShifts(ids["employee"], shift.ShiftDate, shift.ShiftDate));
        db.Add(new ShiftAssignment { ShiftId = shift.Id, UserId = ids["employee"] });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("CK_HardBlock", Assert.IsType<PostgresException>(error.InnerException).ConstraintName);
    }

    [PostgresFact]
    public async Task Existing_assignment_rejects_hard_block_atomically()
    {
        await Open(); using var employee = await Login();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var shift = await db.Set<Shift>().SingleAsync(x => x.ShiftDate == new DateOnly(2026, 10, 25) && x.ShiftType == ShiftType.DAY);
            db.Add(new ShiftAssignment { ShiftId = shift.Id, UserId = ids["employee"] }); await db.SaveChangesAsync();
        }
        await Error(await Send(employee, "/api/availability/2026/10", Input(0, Pick("2026-10-25")), "POST"), "SHIFT_ALREADY_ASSIGNED", HttpStatusCode.Conflict);
        Assert.False((await employee.GetFromJsonAsync<AvailabilityResponse>("/api/availability/2026/10"))!.Submitted);
    }

    [PostgresFact]
    public async Task Cross_year_weeks_share_the_same_three_block_limit()
    {
        await Open(2026, 12); await Open(2027, 1); using var employee = await Login();
        Assert.True((await Send(employee, "/api/availability/2026/12", Input(0, Pick("2026-12-31"), Pick("2026-12-31", "NIGHT")), "POST")).IsSuccessStatusCode);
        await Error(await Send(employee, "/api/availability/2027/1", Input(0, Pick("2027-01-01"), Pick("2027-01-02")), "POST"), "WEEKLY_BLOCK_LIMIT");
        Assert.True((await Send(employee, "/api/availability/2027/1", Input(0, Pick("2027-01-01")), "POST")).IsSuccessStatusCode);
    }

    [PostgresFact]
    public async Task Administrator_with_employee_role_has_no_weekly_limit_override()
    {
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True((await users.AddToRoleAsync((await users.FindByIdAsync(ids["admin"].ToString()))!, "EMPLOYEE")).Succeeded);
        }
        await Open(); using var admin = await Login("admin");
        await Error(await Send(admin, "/api/availability/2026/10", Input(0, Enumerable.Range(25, 4).Select(d => Pick($"2026-10-{d}")).ToArray()), "POST"), "WEEKLY_BLOCK_LIMIT");
        Assert.False((await admin.GetFromJsonAsync<AvailabilityResponse>("/api/availability/2026/10"))!.Submitted);
    }

    [PostgresFact]
    public async Task Direct_database_block_insertion_cannot_invalidate_an_existing_assignment()
    {
        await Open();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var shift = await db.Set<Shift>().SingleAsync(x => x.ShiftDate == new DateOnly(2026, 10, 25) && x.ShiftType == ShiftType.DAY);
        db.Add(new ShiftAssignment { ShiftId = shift.Id, UserId = ids["employee"] }); await db.SaveChangesAsync();
        db.Add(new ShiftPreference { ShiftId = shift.Id, UserId = ids["employee"], PreferenceType = PreferenceType.BLOCKED });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("CK_HardBlock", Assert.IsType<PostgresException>(error.InnerException).ConstraintName);
    }

    [PostgresFact]
    public async Task Upgrade_from_phase_two_preserves_users_preferences_and_backfills_submission()
    {
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261024154101_RequirePasswordChange");
        var period = new SchedulePeriod { Year = 2026, Month = 9 };
        // Populate the historical schema using its columns, then attach the period unchanged.
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"SchedulePeriod\" (\"Id\",\"Year\",\"Month\",\"Status\",\"CreatedAt\",\"UpdatedAt\") VALUES ({period.Id},{period.Year},{period.Month},'DRAFT',{period.CreatedAt},{period.UpdatedAt})");
        db.Attach(period);
        var bounds = ShiftClock.Boundaries(new(2026, 9, 30), ShiftType.DAY);
        var shift = new Shift { SchedulePeriod = period, ShiftDate = new(2026, 9, 30), StartsAt = bounds.StartsAt, EndsAt = bounds.EndsAt };
        db.Add(new ShiftPreference { Shift = shift, UserId = ids["employee"], PreferenceType = PreferenceType.BLOCKED });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await db.Database.MigrateAsync();
        Assert.Equal(6, await db.Users.CountAsync()); Assert.Single(await db.Set<ShiftPreference>().ToArrayAsync());
        var submission = Assert.Single(await db.Set<AvailabilitySubmission>().ToArrayAsync());
        Assert.Equal(ids["employee"], submission.UserId); Assert.Equal(period.Id, submission.SchedulePeriodId); Assert.Equal(1, submission.Version);
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
