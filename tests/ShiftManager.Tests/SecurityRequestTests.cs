using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using ShiftManager.Api;
using ShiftManager.Domain;

namespace ShiftManager.Tests;

public sealed partial class SchedulingTests
{
    [PostgresTheory]
    [InlineData("CREATE")] [InlineData("MODIFY")] [InlineData("CANCEL")]
    public async Task Security_personal_absence_proposals_require_a_different_approver(string kind)
    {
        using var admin = await Login(); await SecurityUsers(admin);
        using var deputy = await Login("coordinator"); using var leader = await Login("leader");
        var request = await SubmitAbsence(deputy);
        if (kind != "CREATE")
        {
            request = await DecideAbsence(admin, request);
            var change = await Send(deputy, $"/api/absences/{request.Id}/changes",
                new AbsenceChange(request.Version, kind, kind == "MODIFY" ? Absence() with { Reason = "VACATION" } : null), "POST");
            Assert.Equal(HttpStatusCode.Created, change.StatusCode);
            request = (await change.Content.ReadFromJsonAsync<AbsenceResponse>())!;
        }
        var report = (await deputy.GetFromJsonAsync<AbsenceConflictReport>($"/api/absences/{request.Id}/conflicts"))!;
        var before = await SecurityFingerprint();
        foreach (var decision in new[] { "APPROVED", "REJECTED" })
        {
            var response = await Send(deputy, $"/api/absences/{request.Id}/decision",
                new AbsenceDecisionRequest(request.Version, decision, decision == "REJECTED" ? "בדיקה" : null, report.Token), "POST");
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("SELF_ABSENCE_DECISION", (await response.Content.ReadFromJsonAsync<ApiError>())!.Code);
            Assert.Equal(before, await SecurityFingerprint());
        }
        var approved = await DecideAbsence(leader, request);
        Assert.Equal(ids["leader"], approved.Decision!.ActorUserId);
    }

    [PostgresTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Security_all_sensitive_http_methods_require_a_valid_antiforgery_token(bool invalidToken)
    {
        var x = await SecuritySetup(); using var admin = x.Admin; var before = await SecurityFingerprint();
        foreach (var call in (await SecurityAdminCalls(x)).Where(c => c.Method != "GET"))
        {
            using var request = new HttpRequestMessage(new HttpMethod(call.Method), call.Path) { Content = JsonContent.Create(call.Body ?? new { }) };
            if (invalidToken) request.Headers.Add("X-CSRF-TOKEN", "invalid-antiforgery-token");
            using var response = await admin.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("CSRF_INVALID", (await response.Content.ReadFromJsonAsync<ApiError>())!.Code);
            Assert.Equal(before, await SecurityFingerprint());
        }
        Assert.Equal("READY", factory.Services.GetRequiredService<Services.AutomaticScheduling.GenerationQueue>().Get(x.Job.Id).Response.State);
    }

    [PostgresFact]
    public async Task Security_malformed_inputs_and_overposting_do_not_mutate_valid_resources_or_leak_exceptions()
    {
        var x = await SecuritySetup(); using var admin = x.Admin; using var employee = await Login("employee");
        var r = await Review(admin);
        Assert.Equal(HttpStatusCode.OK, (await Send(admin, ReviewPath + "/correction", new ReviewTokenRequest(r.Token), "POST")).StatusCode);
        r = await Review(admin); var before = await SecurityFingerprint();
        foreach (var call in new[] {
            new SecurityCall("PUT", $"/api/users/{ids["foreign"]}", new { firstName = "שם", lastName = "שם", username = "foreign", roles = new[] { "ADMIN" } }),
            new("PATCH", $"/api/users/{ids["foreign"]}/status", new { }),
            new("PUT", $"/api/users/{ids["foreign"]}/roles", new { roles = new[] { "SHIFT_LEAD" } }),
            new("PUT", $"/api/users/{ids["foreign"]}", new { firstName = new string('א', 101), lastName = "שם", username = "foreign" }),
            new("PUT", $"{TeamsPath}/members/{ids["foreign"]}", new { teamId = "not-a-guid" }),
            new("PUT", $"{TeamsPath}/members/{ids["foreign"]}", new MembershipRequest(Guid.NewGuid())),
            new("POST", ReviewPath + "/assignments", new { expectedToken = r.Token, operation = "OVERRIDE_ALL", userId = ids["foreign"], definitionId = x.Definition, date = "2026-10-25" }),
            new("POST", ReviewPath + "/assignments", new { expectedToken = r.Token, operation = "ASSIGN", userId = ids["foreign"], definitionId = x.Definition, date = "not-a-date" }),
            new("PUT", $"{ReviewPath}/issues/{x.Issue}", new { expectedVersion = 1, status = "INVALID", response = "בדיקה" }) })
        {
            var response = await SecuritySend(admin, call);
            Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound, $"{call.Method} {call.Path}: {(int)response.StatusCode}");
            var error = await response.Content.ReadFromJsonAsync<ApiError>(); Assert.NotNull(error);
            Assert.DoesNotContain("Exception", error.Message); Assert.DoesNotContain("Npgsql", error.Message);
            Assert.Equal(before, await SecurityFingerprint());
        }
        var rows = Request(await Types(admin));
        foreach (var invalid in new[] { rows.ShiftTypes[0] with { RequiredEmployees = -1 }, rows.ShiftTypes[0] with { RequiredEmployees = 1001 },
            rows.ShiftTypes[0] with { SchedulingPriority = "Emergency" }, rows.ShiftTypes[0] with { Period = "INVALID" } })
        {
            var changed = rows.ShiftTypes.ToArray(); changed[0] = invalid;
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(admin, TypesPath, new UpdateShiftTypesRequest(changed))).StatusCode);
            Assert.Equal(before, await SecurityFingerprint());
        }
        foreach (var pattern in new[] { Absence() with { EndDate = new(2026, 9, 1) }, Absence() with { Reason = "INVALID" }, Absence() with { Explanation = new string('א', 2001) } })
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(employee, "/api/absences", pattern, "POST")).StatusCode);
        Assert.Equal(before, await SecurityFingerprint());
        var search = await admin.GetFromJsonAsync<UserPage>("/api/users?search=" + Uri.EscapeDataString("' OR 1=1; DROP TABLE AspNetUsers;--"));
        Assert.Empty(search!.Items); Assert.Equal(before, await SecurityFingerprint());
    }

    [PostgresFact]
    public async Task Security_workflow_replays_and_conflicting_decisions_have_one_audited_winner()
    {
        var x = await SecuritySetup(); using var admin = x.Admin; using var foreign = await Login("foreign"); using var replacement = await Login("qualified");
        var before = await SecurityFingerprint();
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(foreign, $"/api/shift-exchanges/{x.Exchange.Id}/decision", new ExchangeDecisionRequest(1, "ACCEPT"), "POST")).StatusCode);
        Assert.Equal(before, await SecurityFingerprint());
        var accepted = await Send(replacement, $"/api/shift-exchanges/{x.Exchange.Id}/decision", new ExchangeDecisionRequest(1, "ACCEPT"), "POST");
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var afterConsent = await SecurityFingerprint();
        Assert.Equal(HttpStatusCode.Conflict, (await Send(replacement, $"/api/shift-exchanges/{x.Exchange.Id}/decision", new ExchangeDecisionRequest(1, "ACCEPT"), "POST")).StatusCode);
        Assert.Equal(afterConsent, await SecurityFingerprint());
        var rejected = await DecideAbsence(admin, x.ForeignAbsence, "REJECTED"); var afterRejection = await SecurityFingerprint();
        Assert.Equal(HttpStatusCode.Conflict, (await Send(admin, $"/api/absences/{rejected.Id}/decision", new AbsenceDecisionRequest(rejected.Version, "APPROVED", null), "POST")).StatusCode);
        Assert.Equal(afterRejection, await SecurityFingerprint());
        var report = (await admin.GetFromJsonAsync<AbsenceConflictReport>($"/api/absences/{x.OwnAbsence.Id}/conflicts"))!;
        var approve = new AbsenceDecisionRequest(x.OwnAbsence.Version, "APPROVED", null, report.Token);
        var decisions = await Task.WhenAll(Send(admin, $"/api/absences/{x.OwnAbsence.Id}/decision", approve, "POST"), Send(admin, $"/api/absences/{x.OwnAbsence.Id}/decision", approve, "POST"));
        Assert.Single(decisions, d => d.StatusCode == HttpStatusCode.OK); Assert.Single(decisions, d => d.StatusCode == HttpStatusCode.Conflict);
        var afterApproval = await SecurityFingerprint();
        Assert.Equal(HttpStatusCode.Conflict, (await Send(admin, $"/api/absences/{x.OwnAbsence.Id}/decision", approve, "POST")).StatusCode);
        Assert.Equal(afterApproval, await SecurityFingerprint());
    }
}
