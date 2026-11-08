using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ShiftManager.Api;

namespace ShiftManager.Tests;

public sealed partial class SchedulingTests
{
    private static async Task<PersonalScheduleResponse> Personal(HttpClient client, int? version = null) =>
        (await client.GetFromJsonAsync<PublicationResponse>($"/api/my-schedule/2026/10{(version.HasValue ? $"?version={version}" : "")}"))!.PersonalSchedule!;

    [PostgresFact]
    public async Task Personal_hours_follow_preliminary_final_correction_and_historical_replacements()
    {
        var (admin, employee, replacement, first) = await ExchangeSetup(false);
        using (admin) using (employee) using (replacement)
        {
            Assert.Equal(720, (await Personal(employee)).Summary.TotalPlannedMinutes);
            Assert.Equal(0, (await Personal(replacement)).Summary.TotalAssignedShifts);
            var final = await Publish(admin, await Review(admin), "FINAL");
            Assert.Equal(720, (await Personal(employee)).Summary.TotalPlannedMinutes);
            var assignment = final.Snapshot!.Cells.SelectMany(c => c.Employees).Single();
            await Edit(admin, await Review(admin), "REPLACE", ids["coordinator"], null, 1, assignment.AssignmentId);
            Assert.Equal(720, (await Personal(employee)).Summary.TotalPlannedMinutes);
            Assert.Equal(0, (await Personal(replacement)).Summary.TotalAssignedShifts);
            await Publish(admin, await Review(admin), "FINAL");
            Assert.Equal(0, (await Personal(employee)).Summary.TotalPlannedMinutes);
            Assert.Equal(720, (await Personal(replacement)).Summary.TotalPlannedMinutes);
            Assert.Equal(720, (await Personal(employee, first.VersionNumber)).Summary.TotalPlannedMinutes);
            Assert.Equal(0, (await Personal(replacement, first.VersionNumber)).Summary.TotalPlannedMinutes);
        }
    }

    [PostgresFact]
    public async Task Personal_hours_change_only_after_approved_exchange_is_republished()
    {
        var (admin, employee, replacement, pub) = await ExchangeSetup();
        using (admin) using (employee) using (replacement)
        {
            var request = await RequestExchange(employee, pub);
            request = await ExchangeDecision(replacement, request, "ACCEPT");
            request = await ExchangeDecision(admin, request, "APPROVE", true);
            Assert.Equal(720, (await Personal(employee)).Summary.TotalPlannedMinutes);
            Assert.Equal(0, (await Personal(replacement)).Summary.TotalAssignedShifts);
            var reviews = (await admin.GetFromJsonAsync<ReviewResponse[]>($"/api/admin/shift-exchanges/{request.Id}/publication-review"))!;
            var response = await Send(admin, $"/api/admin/shift-exchanges/{request.Id}/publish",
                new ExchangePublicationRequest(reviews.Select(r => new ExchangePeriodToken(r.Draft.Year, r.Draft.Month, r.Token)).ToArray(), true, true, null), "POST");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(0, (await Personal(employee)).Summary.TotalAssignedShifts);
            Assert.Equal(720, (await Personal(replacement)).Summary.TotalPlannedMinutes);
            Assert.Equal(720, (await Personal(employee, pub.VersionNumber)).Summary.TotalPlannedMinutes);
        }
    }

    [PostgresFact]
    public async Task Personal_hours_require_publication_session_identity_and_existing_role_boundaries()
    {
        var (admin, definition, r) = await ManualSetup(); using (admin)
        {
            using var anonymous = Client(); using var employee = await Login("employee"); using var coordinator = await Login("coordinator");
            const string path = "/api/my-schedule/2026/10";
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await coordinator.GetAsync(path)).StatusCode);
            r = await Edit(admin, r, "ASSIGN", ids["employee"], definition, 1);
            Assert.Equal(HttpStatusCode.NotFound, (await employee.GetAsync(path)).StatusCode);
            await Publish(admin, await Ack(admin, r), "PRELIMINARY");
            var own = (await employee.GetFromJsonAsync<PublicationResponse>(path + $"?employeeId={ids["coordinator"]}"))!;
            Assert.Equal(720, own.PersonalSchedule!.Summary.TotalPlannedMinutes);
            Assert.Equal(Guid.Empty, own.PublishedByUserId); Assert.Null(own.PublishedByName);
            foreach (var suffix in new[] { "/2026/0", "/2026/13", "/0/1", "/9999/12", "/2026/10?version=0" })
                Assert.Equal(HttpStatusCode.BadRequest, (await employee.GetAsync("/api/my-schedule" + suffix)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await employee.GetAsync($"/api/my-schedule/{ids["coordinator"]}/2026/10")).StatusCode);
            await SetupTeam(admin);
            using var leader = await Login("leader"); using var deputy = await Login("coordinator");
            Assert.Equal(HttpStatusCode.Forbidden, (await leader.GetAsync(path)).StatusCode);
            Assert.Equal(0, (await Personal(deputy)).Summary.TotalAssignedShifts);
            Assert.Equal(HttpStatusCode.Forbidden, (await deputy.GetAsync(ReviewPath)).StatusCode);
        }
    }

    [PostgresFact]
    public async Task Personal_schedule_response_latency_is_measured_over_a_real_monthly_snapshot()
    {
        var (admin, employee, replacement, _) = await ExchangeSetup();
        using (admin) using (employee) using (replacement)
        {
            var timings = new List<double>();
            for (var i = 0; i < 21; i++)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var response = await Personal(employee);
                if (i > 0) timings.Add(watch.Elapsed.TotalMilliseconds);
                Assert.Equal(1, response.Summary.TotalAssignedShifts);
            }
            timings.Sort();
            var folder = Path.Combine("TestResults", "phase8a"); Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(Path.Combine(folder, "personal-schedule-latency.json"), JsonSerializer.Serialize(new {
                Samples = timings.Count, MedianMs = (timings[9] + timings[10]) / 2, MaximumMs = timings[^1],
                Fixture = "Real Identity cookie; immutable 51-row October snapshot; one own assignment; in-process HTTP; disposable PostgreSQL"
            }));
        }
    }
}
