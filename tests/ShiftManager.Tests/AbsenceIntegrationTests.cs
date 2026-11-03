using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShiftManager.Api;
using ShiftManager.Data;
using ShiftManager.Domain;
namespace ShiftManager.Tests;
public sealed partial class SchedulingTests
{
    private static AbsencePattern Absence(bool recurring = false, int[]? weekdays = null) => new("STUDIES", new(2026, 10, 1), recurring ? null : new(2026, 10, 3), recurring, weekdays ?? (recurring ? [2] : []), true, null, null, "לימודים באוניברסיטה");
    private static async Task<AbsenceResponse> SubmitAbsence(HttpClient employee, AbsencePattern? pattern = null)
    {
        var response = await Send(employee, "/api/absences", pattern ?? Absence(), "POST");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AbsenceResponse>())!;
    }
    private static async Task<AbsenceResponse> DecideAbsence(HttpClient approver, AbsenceResponse request, string decision = "APPROVED", bool remove = false)
    {
        var report = (await approver.GetFromJsonAsync<AbsenceConflictReport>($"/api/absences/{request.Id}/conflicts"))!;
        var response = await Send(approver, $"/api/absences/{request.Id}/decision", new AbsenceDecisionRequest(request.Version, decision, decision == "REJECTED" ? "לא ניתן לאשר" : null, report.Token, remove), "POST");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AbsenceResponse>())!;
    }
    [PostgresFact]
    public async Task Absence_lifecycle_preserves_approved_restriction_until_replacement_or_cancellation_is_approved()
    {
        using var admin = await Login(); using var employee = await Login("employee");
        var request = await SubmitAbsence(employee, Absence(true, [1, 3]));
        Assert.Equal("PENDING", request.Status);
        var approved = await DecideAbsence(admin, request);
        Assert.Equal(ids["admin"], approved.Decision!.ActorUserId);
        Assert.Equal("APPROVED", approved.Status);
        var changed = await Send(employee, $"/api/absences/{approved.Id}/changes", new AbsenceChange(approved.Version, "MODIFY", Absence() with { Reason = "VACATION" }), "POST");
        Assert.Equal(HttpStatusCode.Created, changed.StatusCode);
        var modification = (await changed.Content.ReadFromJsonAsync<AbsenceResponse>())!;
        Assert.Equal("APPROVED", (await employee.GetFromJsonAsync<AbsenceResponse[]>("/api/absences/mine"))!.Single(r => r.Id == approved.Id).Status);
        await DecideAbsence(admin, modification, "REJECTED");
        var cancel = (await (await Send(employee, $"/api/absences/{approved.Id}/changes", new AbsenceChange(approved.Version, "CANCEL", null), "POST")).Content.ReadFromJsonAsync<AbsenceResponse>())!;
        Assert.Equal(HttpStatusCode.Conflict, (await Send(employee, $"/api/absences/{approved.Id}/cancel-pending", new AbsenceVersion(approved.Version), "POST")).StatusCode);
        await DecideAbsence(admin, cancel);
        Assert.Equal("CANCELLED", (await employee.GetFromJsonAsync<AbsenceResponse[]>("/api/absences/mine"))!.Single(r => r.Id == approved.Id).Status);
        var next = await SubmitAbsence(employee);
        var replacementParent = await DecideAbsence(admin, next);
        var proposal = (await (await Send(employee, $"/api/absences/{next.Id}/changes", new AbsenceChange(replacementParent.Version, "MODIFY", Absence() with { StartDate = new(2026, 11, 1), EndDate = new(2026, 11, 2) }), "POST")).Content.ReadFromJsonAsync<AbsenceResponse>())!;
        Assert.Equal(HttpStatusCode.Conflict, (await Send(employee, $"/api/absences/{next.Id}/changes", new AbsenceChange(replacementParent.Version, "CANCEL", null), "POST")).StatusCode);
        var replacement = await DecideAbsence(admin, proposal);
        Assert.Equal("APPROVED", replacement.Status);
        Assert.Equal(next.Id, replacement.ReplacesId);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(2, await db.Set<AbsenceWeekday>().CountAsync(d => d.AbsenceRequestId == request.Id));
        Assert.Equal("CANCELLED", (await db.Set<AbsenceRequest>().SingleAsync(r => r.Id == next.Id)).Status);
    }
    [PostgresFact]
    public async Task Absence_team_scope_pending_cancel_rejection_and_duplicate_decisions_are_enforced()
    {
        using var admin = await Login(); await SetupTeam(admin);
        using var employee = await Login("employee"); using var deputy = await Login("coordinator");
        var request = await SubmitAbsence(employee);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync("/api/absences/manage")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync($"/api/absences/{request.Id}/conflicts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await deputy.GetAsync(ReviewPath)).StatusCode);
        Assert.Single((await deputy.GetFromJsonAsync<AbsenceResponse[]>("/api/absences/manage"))!);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(deputy, $"/api/absences/{request.Id}/decision", new AbsenceDecisionRequest(request.Version, "REJECTED", " "), "POST")).StatusCode);
        var report = (await deputy.GetFromJsonAsync<AbsenceConflictReport>($"/api/absences/{request.Id}/conflicts"))!;
        var body = new AbsenceDecisionRequest(request.Version, "APPROVED", null, report.Token);
        var responses = await Task.WhenAll(Send(admin, $"/api/absences/{request.Id}/decision", body, "POST"), Send(deputy, $"/api/absences/{request.Id}/decision", body, "POST"));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK); Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        var pending = await SubmitAbsence(employee);
        Assert.Equal(HttpStatusCode.OK, (await Send(employee, $"/api/absences/{pending.Id}/cancel-pending", new AbsenceVersion(pending.Version), "POST")).StatusCode);
        var rejected = await DecideAbsence(deputy, await SubmitAbsence(employee), "REJECTED");
        Assert.Equal("לא ניתן לאשר", rejected.Decision!.RejectionReason);
        // Move the employee away: previous team authority is immediately revoked.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var membership = await db.Set<TeamMembership>().SingleAsync(m => m.UserId == ids["employee"]);
            var otherTeam = await db.Set<OrganizationalTeam>().Where(t => t.Id != membership.TeamId).Select(t => t.Id).FirstAsync();
            Assert.Equal(HttpStatusCode.OK, (await Send(admin, $"{TeamsPath}/members/{ids["employee"]}", new MembershipRequest(otherTeam))).StatusCode);
        }
        Assert.Empty((await deputy.GetFromJsonAsync<AbsenceResponse[]>("/api/absences/manage"))!);
        Assert.Equal(HttpStatusCode.Forbidden, (await deputy.GetAsync($"/api/absences/{request.Id}/conflicts")).StatusCode);
    }
    [PostgresFact]
    public async Task Absence_server_validates_dates_recurrence_times_and_ownership()
    {
        using var admin = await Login(); using var employee = await Login("employee");
        foreach (var invalid in new[] { Absence() with { EndDate = null }, Absence(true, []), Absence(true, [1, 1]), Absence(true, [7]),
            Absence() with { EndDate = new(2026, 9, 1) }, Absence() with { FullDay = false }, Absence() with { Reason = "INVALID" },
            Absence() with { FullDay = false, StartTime = new(8, 0), EndTime = new(8, 0) }, Absence() with { Explanation = new string('א', 2001) } })
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(employee, "/api/absences", invalid, "POST")).StatusCode);
        var request = await SubmitAbsence(employee);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(admin, "/api/absences", Absence(), "POST")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(admin, $"/api/absences/{request.Id}/cancel-pending", new AbsenceVersion(request.Version), "POST")).StatusCode);
    }
}
