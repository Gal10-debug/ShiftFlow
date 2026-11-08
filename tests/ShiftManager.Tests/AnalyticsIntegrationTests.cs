using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShiftManager.Api;
using ShiftManager.Data;
using ShiftManager.Domain;

namespace ShiftManager.Tests;

public sealed partial class SchedulingTests
{
    private const string PersonalStats = "/api/my-statistics/2026/10";
    private const string AdminStats = "/api/admin/statistics/2026/10";
    [PostgresFact]
    public async Task Analytics_publication_replacement_history_visibility_and_annual_consistency()
    {
        var (admin, employee, replacement, preliminary) = await ExchangeSetup(false);
        using (admin) using (employee) using (replacement)
        {
            var personal = (await employee.GetFromJsonAsync<PersonalAnalytics>(PersonalStats))!;
            Assert.Equal(720, personal.Totals.TotalPlannedMinutes); Assert.Null(personal.Comparison!.ShiftCountChange);
            Assert.Equal(preliminary.Id, personal.Months[0].Publication!.Id);
            await Publish(admin, await Review(admin), "FINAL");
            var assignment = preliminary.Snapshot!.Cells.SelectMany(c => c.Employees).Single();
            await Edit(admin, await Review(admin), "REPLACE", ids["coordinator"], null, 1, assignment.AssignmentId);
            Assert.Equal(720, (await employee.GetFromJsonAsync<PersonalAnalytics>(PersonalStats))!.Totals.TotalPlannedMinutes);
            var final = await Publish(admin, await Review(admin), "FINAL");
            Assert.Equal(0, (await employee.GetFromJsonAsync<PersonalAnalytics>(PersonalStats))!.Totals.TotalAssignedShifts);
            Assert.Equal(720, (await replacement.GetFromJsonAsync<PersonalAnalytics>(PersonalStats))!.Totals.TotalPlannedMinutes);
            Assert.Equal(720, (await employee.GetFromJsonAsync<PersonalAnalytics>(PersonalStats + "?version=1"))!.Totals.TotalPlannedMinutes);
            Assert.Equal(720, (await admin.GetFromJsonAsync<AdminAnalytics>(AdminStats + "?version=1"))!.Totals.TotalPlannedMinutes);
            var annual = (await replacement.GetFromJsonAsync<PersonalAnalytics>("/api/my-statistics/2026"))!;
            Assert.Equal(12, annual.Months.Length); Assert.Single(annual.Months, m => m.Publication is not null);
            Assert.Equal(annual.Totals.KnownPlannedMinutes, annual.Months.Sum(m => m.Totals.KnownPlannedMinutes));
            Assert.Equal(720, annual.Totals.TotalPlannedMinutes);
            Assert.Equal(HttpStatusCode.NoContent, (await Send(admin, ControlPath(final.Id), new PublicationControlRequest(0, "HIDE", "Visibility test"), "POST")).StatusCode);
            Assert.Null((await employee.GetFromJsonAsync<PersonalAnalytics>(PersonalStats))!.Months[0].Publication);
            Assert.Null((await admin.GetFromJsonAsync<AdminAnalytics>(AdminStats))!.Coverage);
            Assert.Equal(HttpStatusCode.NotFound, (await employee.GetAsync(PersonalStats + "?version=1")).StatusCode);
        }
    }
    [PostgresFact]
    public async Task Analytics_exchange_stays_on_original_publication_until_atomic_republication()
    {
        var (admin, employee, replacement, publication) = await ExchangeSetup();
        using (admin) using (employee) using (replacement)
        {
            var exchange = await RequestExchange(employee, publication);
            exchange = await ExchangeDecision(replacement, exchange, "ACCEPT");
            exchange = await ExchangeDecision(admin, exchange, "APPROVE", true);
            Assert.Equal(1, (await employee.GetFromJsonAsync<PersonalAnalytics>(PersonalStats))!.Totals.TotalAssignedShifts);
            var reviews = (await admin.GetFromJsonAsync<ReviewResponse[]>($"/api/admin/shift-exchanges/{exchange.Id}/publication-review"))!;
            var response = await Send(admin, $"/api/admin/shift-exchanges/{exchange.Id}/publish", new ExchangePublicationRequest(
                reviews.Select(r => new ExchangePeriodToken(r.Draft.Year, r.Draft.Month, r.Token)).ToArray(), true, true, null), "POST");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(0, (await employee.GetFromJsonAsync<PersonalAnalytics>(PersonalStats))!.Totals.TotalAssignedShifts);
            Assert.Equal(1, (await replacement.GetFromJsonAsync<PersonalAnalytics>(PersonalStats))!.Totals.TotalAssignedShifts);
        }
    }
    [PostgresFact]
    public async Task Analytics_scope_filters_draft_protection_and_invalid_inputs()
    {
        var (admin, definition, r) = await ManualSetup(); using (admin)
        {
            using var anonymous = Client(); using var employee = await Login("employee"); using var coordinator = await Login("coordinator");
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(PersonalStats)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(AdminStats)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await coordinator.GetAsync(PersonalStats)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await coordinator.GetAsync(AdminStats)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync(AdminStats + $"?employeeId={ids["employee"]}")).StatusCode);
            r = await Edit(admin, r, "ASSIGN", ids["employee"], definition, 2);
            Assert.Null((await employee.GetFromJsonAsync<PersonalAnalytics>(PersonalStats))!.Months[0].Publication);
            await Publish(admin, await Ack(admin, r), "PRELIMINARY");
            var team = await SetupTeam(admin);
            using var leader = await Login("leader"); using var deputy = await Login("coordinator");
            Assert.Equal(HttpStatusCode.Forbidden, (await leader.GetAsync(AdminStats + $"?teamId={team.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await leader.GetAsync(PersonalStats)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await deputy.GetAsync(AdminStats + $"?teamId={team.Id}")).StatusCode);
            Assert.Equal(0, (await deputy.GetFromJsonAsync<PersonalAnalytics>(PersonalStats))!.Totals.TotalAssignedShifts);
            var org = (await admin.GetFromJsonAsync<AdminAnalytics>(AdminStats))!;
            Assert.Equal(1, org.Distribution.ScheduledEmployees); Assert.Equal(1, org.Totals.WeekendShiftCount);
            Assert.NotNull(org.Coverage); Assert.Equal(org.Coverage!.RequiredPositions - 1, org.Coverage.UnfilledPositions);
            var filtered = (await admin.GetFromJsonAsync<AdminAnalytics>(AdminStats + $"?teamId={team.Id}"))!;
            Assert.Equal(1, filtered.Totals.TotalAssignedShifts); Assert.Null(filtered.Coverage);
            Assert.All(filtered.Employees, e => Assert.Equal(team.Id, e.TeamId));
            var other = (await Teams(admin)).First(t => t.Id != team.Id);
            Assert.Equal(0, (await admin.GetFromJsonAsync<AdminAnalytics>(AdminStats + $"?teamId={other.Id}"))!.Totals.TotalAssignedShifts);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync(AdminStats + $"?teamId={other.Id}&employeeId={ids["employee"]}")).StatusCode);
            foreach (var suffix in new[] { "/0/1", "/9999/1", "/2026/0", "/2026/13", "/2026/10?version=0", "/2026/10?version=abc", "/2026/10?version=" })
                Assert.Equal(HttpStatusCode.BadRequest, (await employee.GetAsync("/api/my-statistics" + suffix)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await employee.GetAsync(PersonalStats + $"?employeeId={ids["coordinator"]}")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await employee.GetAsync(PersonalStats + $"?teamId={team.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync(AdminStats + "?teamId=bad")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(AdminStats + $"?teamId={Guid.NewGuid()}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(AdminStats + $"?employeeId={Guid.NewGuid()}")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await employee.GetAsync("/api/my-statistics/2026?version=1")).StatusCode);
        }
    }
    [PostgresFact]
    public async Task Analytics_four_endpoint_latencies_are_measured_over_twelve_published_months()
    {
        using var admin = await Login(); using var employee = await Login("employee");
        // Own disposable fixture only. Immutable synthetic snapshots avoid solver setup in a read benchmark.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var workloadEmployees = new List<Guid> { ids["employee"] };
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            for (var i = 1; i < 24; i++)
            {
                var user = new ApplicationUser { UserName = $"analytics-benchmark-{i}", FirstName = "עובד", LastName = $"בדיקה {i}" };
                Assert.True((await manager.CreateAsync(user)).Succeeded);
                Assert.True((await manager.AddToRoleAsync(user, "EMPLOYEE")).Succeeded);
                workloadEmployees.Add(user.Id);
            }
            for (var month = 1; month <= 12; month++)
            {
                var period = new SchedulePeriod { Year = 2026, Month = month, Status = ScheduleStatus.FINAL_PUBLISHED };
                var cells = Enumerable.Range(0, DateTime.DaysInMonth(2026, month)).SelectMany(day => Enumerable.Range(1, 51).Select(slot => {
                    var date = new DateOnly(2026, month, day + 1);
                    var range = slot % 2 == 0 ? ShiftTimeRange.NIGHT : ShiftTimeRange.DAY;
                    var t = ShiftTimeRanges.Boundaries(date, range);
                    var employeeId = workloadEmployees[(day * 12 + slot - 1) % workloadEmployees.Count];
                    return new SnapshotCell(Guid.Parse($"40000000-0000-0000-0000-{slot:000000000000}"), date, "Published row", slot, range.ToString(), range.ToString(),
                        t.StartsAt, t.EndsAt, 1, false, slot <= 12 ? [new(Guid.NewGuid(), employeeId, "Employee", false)] : []);
                })).ToArray();
                db.Add(period); db.Add(new SchedulePublication { SchedulePeriodId = period.Id, PublishedByUserId = ids["admin"], VersionNumber = 1,
                    PublicationType = "FINAL", Snapshot = JsonSerializer.Serialize(new ScheduleSnapshot(2026, month, cells, [])), PublishedAt = clock.Now.UtcDateTime });
            }
            await db.SaveChangesAsync();
        }
        var measurements = new List<object>();
        foreach (var (client, path) in new[] { (employee, PersonalStats), (employee, "/api/my-statistics/2026"), (admin, AdminStats), (admin, "/api/admin/statistics/2026") })
        {
            var times = new List<double>();
            for (var i = 0; i < 21; i++)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var response = await client.GetAsync(path); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.True(json.RootElement.GetProperty("totals").GetProperty("totalAssignedShifts").GetInt32() > 0);
                if (i > 0) times.Add(watch.Elapsed.TotalMilliseconds);
            }
            times.Sort(); measurements.Add(new { Endpoint = path, Samples = times.Count, MedianMs = (times[9] + times[10]) / 2, MaximumMs = times[^1] });
        }
        var directory = Path.Combine("TestResults", "phase8b"); Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "analytics-latency.json"), JsonSerializer.Serialize(new {
            Conditions = "In-process HTTP; real Identity cookies; disposable PostgreSQL 17; 12 publications; 18,615 cells; 24 scheduled employees; 4,380 assignments; one warm-up per endpoint", Measurements = measurements }));
    }
}

public sealed partial class SchedulingTests
{
    [PostgresFact]
    public async Task Analytics_month_comparison_and_year_sums_preserve_special_durations_and_admin_filters()
    {
        using var admin = await Login(); using var employee = await Login("employee");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            foreach (var (year, month) in new[] { (2026, 9), (2026, 10), (1, 12), (2, 1) })
            {
                var period = new SchedulePeriod { Year = year, Month = month, Status = ScheduleStatus.FINAL_PUBLISHED };
                var date = new DateOnly(year, month, 1); var t = ShiftTimeRanges.Boundaries(date, ShiftTimeRange.DAY);
                SnapshotCell Cell(int slot) => new(Guid.Parse($"40000000-0000-0000-0000-{slot:000000000000}"), date,
                    "Frozen row", slot, "DAY", "DAY", t.StartsAt, t.EndsAt, 1, false, [new(Guid.NewGuid(), ids["employee"], "Employee", false)]);
                var timed = Cell(1);
                var cells = month == 10 ? new[] { timed, Cell(14) } : new[] { timed };
                db.Add(period); db.Add(new SchedulePublication { SchedulePeriodId = period.Id, PublishedByUserId = ids["admin"],
                    VersionNumber = 1, PublicationType = "FINAL", PublishedAt = clock.Now.UtcDateTime,
                    Snapshot = JsonSerializer.Serialize(new ScheduleSnapshot(year, month, cells, [])) });
            }
            await db.SaveChangesAsync();
        }
        var boundary = (await employee.GetFromJsonAsync<PersonalAnalytics>("/api/my-statistics/2/1"))!;
        Assert.Equal(1, boundary.Totals.TotalAssignedShifts); Assert.Null(boundary.Comparison!.PreviousMonth.Publication);
        var personal = (await employee.GetFromJsonAsync<PersonalAnalytics>(PersonalStats))!;
        Assert.Equal(1, personal.Comparison!.ShiftCountChange); Assert.Equal(0, personal.Comparison.KnownPlannedMinutesChange);
        Assert.False(personal.Comparison.BothDurationsComplete); Assert.Equal(1, personal.Totals.UnknownDurationAssignments);
        var annual = (await employee.GetFromJsonAsync<PersonalAnalytics>("/api/my-statistics/2026"))!;
        Assert.Equal(3, annual.Totals.TotalAssignedShifts); Assert.Equal(1440, annual.Totals.KnownPlannedMinutes);
        Assert.Null(annual.Totals.TotalPlannedMinutes); Assert.Equal(annual.Months.Sum(m => m.Totals.UnknownDurationAssignments), annual.Totals.UnknownDurationAssignments);
        var org = (await admin.GetFromJsonAsync<AdminAnalytics>("/api/admin/statistics/2026"))!;
        Assert.Equal(annual.Totals.KnownPlannedMinutes, org.Totals.KnownPlannedMinutes);
        Assert.Equal(1, org.Totals.UnknownDurationAssignments); Assert.Equal(org.Months.Sum(m => m.Coverage?.RequiredPositions ?? 0), org.Coverage!.RequiredPositions);
        var own = (await admin.GetFromJsonAsync<AdminAnalytics>(AdminStats + $"?employeeId={ids["employee"]}"))!;
        Assert.Single(own.Employees); Assert.Equal(2, own.Totals.TotalAssignedShifts); Assert.Null(own.Coverage);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/admin/statistics/2026?version=1")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await employee.GetAsync(PersonalStats + "?version=1&version=2")).StatusCode);
    }
}
