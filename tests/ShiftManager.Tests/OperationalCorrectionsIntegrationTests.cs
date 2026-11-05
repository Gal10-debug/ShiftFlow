using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ShiftManager.Api;
using ShiftManager.Data;
using ShiftManager.Domain;
using ShiftManager.Services;
using ShiftManager.Services.AutomaticScheduling;
namespace ShiftManager.Tests;

public sealed partial class SchedulingTests
{
    private static string ControlPath(Guid id) => $"{ReviewPath}/publications/{id}/control";
    private const string ExchangePath = "/api/shift-exchanges";
    private async Task<(HttpClient Admin, HttpClient Employee, HttpClient Replacement, PublicationResponse Publication)> ExchangeSetup(bool final = true, string? hardCase = null)
    {
        clock.Now = DateTimeOffset.Parse("2026-09-29T00:00:00Z");
        var (admin, definition, r) = await ManualSetup();
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var replacement = (await users.FindByIdAsync(ids["coordinator"].ToString()))!;
            Assert.True((await users.AddToRoleAsync(replacement, "EMPLOYEE")).Succeeded);
        }
        r = await Review(admin);
        if (hardCase is "NIGHTS" or "SIX_NINE")
        {
            using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var d = await db.Set<ShiftTypeDefinition>().SingleAsync(d => d.Id == definition);
            if (hardCase == "NIGHTS") { d.Period = ShiftType.NIGHT; d.TimeRange = ShiftTimeRange.NIGHT; }
            else d.Semantic = SchedulingRules.SixNine;
            await db.SaveChangesAsync(); r = await Review(admin);
        }
        r = await Edit(admin, r, "ASSIGN", ids["employee"], definition, hardCase == "WEEKENDS" ? 2 : 1); r = await Ack(admin, r);
        var publication = await Publish(admin, r, "PRELIMINARY");
        if (final) publication = await Publish(admin, await Review(admin), "FINAL");
        return (admin, await Login("employee"), await Login("coordinator"), publication);
    }
    private async Task<ExchangeResponse> RequestExchange(HttpClient employee, PublicationResponse pub)
    {
        var assignment = pub.Snapshot!.Cells.SelectMany(c => c.Employees).Single(e => e.UserId == ids["employee"]);
        var response = await Send(employee, ExchangePath + "/2026/10", new ExchangeCreateRequest(pub.Id, assignment.AssignmentId, ids["coordinator"]), "POST");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); return (await response.Content.ReadFromJsonAsync<ExchangeResponse>())!;
    }
    private static async Task<ExchangeResponse> ExchangeDecision(HttpClient client, ExchangeResponse request, string decision, bool admin = false)
    {
        var response = await Send(client, $"/api/{(admin ? "admin/" : "")}shift-exchanges/{request.Id}/decision", new ExchangeDecisionRequest(request.Version, decision), "POST");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); return (await response.Content.ReadFromJsonAsync<ExchangeResponse>())!;
    }
    [PostgresFact]
    public async Task Administrators_cannot_join_or_lead_teams_and_legacy_dual_role_is_never_scheduled_or_delegated()
    {
        using var admin = await Login(); var t = await SetupTeam(admin);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(admin, $"{TeamsPath}/members/{ids["admin"]}", new MembershipRequest(t.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(admin, $"{TeamsPath}/{t.Id}", new UpdateTeamRequest(t.Name, ids["admin"], t.DeputyUserId, t.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(admin, $"/api/users/{ids["leader"]}/roles", new RolesRequest(["ADMIN", "EMPLOYEE"]))).StatusCode);
        // Simulate affected pre-upgrade data without requiring a destructive data migration.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await manager.FindByIdAsync(ids["admin"].ToString()))!; await manager.AddToRoleAsync(user, "EMPLOYEE");
            db.Add(new TeamMembership { TeamId = t.Id, UserId = user.Id }); await db.SaveChangesAsync();
            var team = await db.Set<OrganizationalTeam>().SingleAsync(x => x.Id == t.Id); team.LeaderUserId = user.Id; await db.SaveChangesAsync();
        }
        using var legacy = await Login();
        var me = (await legacy.GetFromJsonAsync<UserResponse>("/api/auth/me"))!;
        Assert.False(me.IsTeamLeader); Assert.False(me.CanManageTeamAvailability);
        Assert.Equal(HttpStatusCode.Forbidden, (await legacy.GetAsync("/api/team-availability/members")).StatusCode);
        await ConfigureWindow(legacy, open: true);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(legacy, "/api/availability/2026/10", Choices(), "POST")).StatusCode);
        using var read = factory.Services.CreateScope();
        Assert.DoesNotContain((await read.ServiceProvider.GetRequiredService<SchedulingInputs>().Read(2026, 10)).Employees, e => e.Id == ids["admin"]);
        Assert.Equal(4, await read.ServiceProvider.GetRequiredService<ApplicationDbContext>().Set<OrganizationalTeam>().CountAsync());
    }
    [PostgresFact]
    public async Task Final_hide_restore_delete_revoke_every_employee_path_preserve_snapshot_and_never_fall_back()
    {
        var (admin, employee, replacement, pub) = await ExchangeSetup(); using (admin) using (employee) using (replacement)
        {
            var original = JsonSerializer.Serialize(pub.Snapshot);
            var assignment = pub.Snapshot!.Cells.SelectMany(c => c.Employees).Single();
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(employee, ControlPath(pub.Id), new PublicationControlRequest(0, "HIDE", "בדיקה"), "POST")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(admin, ControlPath(pub.Id), new PublicationControlRequest(0, "DELETE", " "), "POST")).StatusCode);
            var pending = await RequestExchange(employee, pub);
            Assert.Equal(HttpStatusCode.NoContent, (await Send(admin, ControlPath(pub.Id), new PublicationControlRequest(0, "HIDE", "תיקון פרסום"), "POST")).StatusCode);
            foreach (var path in new[] { "/api/my-schedule/2026/10", "/api/my-schedule/2026/10?version=1", "/api/my-schedule/2026/10?version=2", "/api/my-schedule/2026/10/versions", "/api/my-schedule/2026/10/issues", ExchangePath + "/2026/10/assignments" })
                Assert.Equal(HttpStatusCode.NotFound, (await employee.GetAsync(path)).StatusCode);
            Assert.Empty((await employee.GetFromJsonAsync<JsonElement[]>("/api/my-schedule/periods"))!);
            var hiddenRequests = (await employee.GetFromJsonAsync<ExchangeResponse[]>(ExchangePath))!;
            Assert.Equal("CANCELLED", hiddenRequests.Single().Status); Assert.Empty(hiddenRequests.Single().Assignments);
            Assert.Equal(HttpStatusCode.NotFound, (await Send(employee, ExchangePath + "/2026/10", new ExchangeCreateRequest(pub.Id, assignment.AssignmentId, ids["coordinator"]), "POST")).StatusCode);
            var r = await Review(admin); r = await Edit(admin, r, "NOTE", null, null, 1, assignment.AssignmentId);
            Assert.Equal(HttpStatusCode.NotFound, (await employee.GetAsync("/api/my-schedule/2026/10")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await Send(admin, ControlPath(pub.Id), new PublicationControlRequest(1, "RESTORE", "הושלמה בדיקה"), "POST")).StatusCode);
            Assert.Equal(pub.Id, (await employee.GetFromJsonAsync<PublicationResponse>("/api/my-schedule/2026/10"))!.Id);
            Assert.Equal(HttpStatusCode.Conflict, (await Send(admin, ControlPath(pub.Id), new PublicationControlRequest(1, "DELETE", "ישן"), "POST")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await Send(admin, ControlPath(pub.Id), new PublicationControlRequest(2, "DELETE", "פרסום שגוי"), "POST")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await employee.GetAsync("/api/my-schedule/2026/10")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await employee.GetAsync("/api/my-schedule/2026/10?version=1")).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await Send(admin, ControlPath(pub.Id), new PublicationControlRequest(3, "RESTORE", "אי אפשר"), "POST")).StatusCode);
            var history = (await admin.GetFromJsonAsync<PublicationResponse[]>(ReviewPath + "/publications"))!;
            Assert.Equal(original, JsonSerializer.Serialize(history.Single(p => p.Id == pub.Id).Snapshot)); Assert.True(history[0].Deleted);
            var audits = (await admin.GetFromJsonAsync<ScheduleEditAudit[]>(ReviewPath + "/audit"))!;
            Assert.Equal(3, audits.Count(a => a.Operation.StartsWith("PUBLICATION_")));
            Assert.All(audits.Where(a => a.Operation.StartsWith("PUBLICATION_")), a => { Assert.Equal(ids["admin"], a.ActorUserId); Assert.Contains(pub.Id.ToString(), a.After); Assert.Contains("Reason", a.After); });
            var corrected = await Publish(admin, await Review(admin), "FINAL");
            Assert.Equal(3, corrected.VersionNumber); Assert.Equal(corrected.Id, (await employee.GetFromJsonAsync<PublicationResponse>("/api/my-schedule/2026/10"))!.Id);
            Assert.Equal(new[] {3,1}, await employee.GetFromJsonAsync<int[]>("/api/my-schedule/2026/10/versions"));
            Assert.Equal(HttpStatusCode.NotFound, (await employee.GetAsync("/api/my-schedule/2026/10?version=2")).StatusCode);
        }
    }
    [PostgresFact]
    public async Task Deleting_older_publication_keeps_latest_active_and_only_revokes_that_history_entry()
    {
        var (admin, employee, replacement, pub) = await ExchangeSetup(); using (admin) using (employee) using (replacement)
        {
            var old = (await admin.GetFromJsonAsync<PublicationResponse[]>(ReviewPath + "/publications"))!.Last();
            Assert.Equal(HttpStatusCode.NoContent, (await Send(admin, ControlPath(old.Id), new PublicationControlRequest(0, "DELETE", "היסטוריה שגויה"), "POST")).StatusCode);
            Assert.Equal(pub.Id, (await employee.GetFromJsonAsync<PublicationResponse>("/api/my-schedule/2026/10"))!.Id);
            Assert.Equal(HttpStatusCode.NotFound, (await employee.GetAsync("/api/my-schedule/2026/10?version=1")).StatusCode);
        }
    }
    [PostgresFact]
    public async Task Exchange_requires_replacement_consent_and_admin_approval_then_explicit_republication()
    {
        var (admin, employee, replacement, pub) = await ExchangeSetup(); using (admin) using (employee) using (replacement)
        {
            var eligible = (await employee.GetFromJsonAsync<ExchangeAssignmentResponse[]>(ExchangePath + "/2026/10/assignments"))!; Assert.Single(eligible);
            var candidates = (await employee.GetFromJsonAsync<CandidateResponse[]>($"{ExchangePath}/2026/10/candidates?publicationId={pub.Id}&assignmentId={eligible[0].AssignmentId}"))!;
            Assert.Contains(candidates, c => c.UserId == ids["coordinator"]); Assert.DoesNotContain(candidates, c => c.UserId == ids["admin"]);
            var request = await RequestExchange(employee, pub);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(employee, $"{ExchangePath}/{request.Id}/decision", new ExchangeDecisionRequest(request.Version,"ACCEPT"),"POST")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(employee, $"/api/admin/shift-exchanges/{request.Id}/decision", new ExchangeDecisionRequest(request.Version,"APPROVE"),"POST")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(admin, $"/api/admin/shift-exchanges/{request.Id}/decision", new ExchangeDecisionRequest(request.Version,"APPROVE"),"POST")).StatusCode);
            request = await ExchangeDecision(replacement, request, "ACCEPT"); Assert.Equal("AWAITING_ADMIN", request.Status);
            Assert.Equal(ids["employee"], (await Review(admin)).Draft.Cells.SelectMany(c => c.Employees).Single().UserId);
            request = await ExchangeDecision(admin, request, "APPROVE", true); Assert.Equal("APPROVED_PENDING_PUBLICATION", request.Status);
            var draft = await Review(admin); Assert.Equal("UNDER_CORRECTION", draft.State);
            Assert.Equal(ids["coordinator"], draft.Draft.Cells.SelectMany(c => c.Employees).Single().UserId);
            Assert.Equal(ids["employee"], (await employee.GetFromJsonAsync<PublicationResponse>("/api/my-schedule/2026/10"))!.Snapshot!.Cells.SelectMany(c => c.Employees).Single().UserId);
            draft = await Ack(admin, draft); var updated = await Publish(admin, draft, "FINAL");
            Assert.Equal(ids["coordinator"], updated.Snapshot!.Cells.SelectMany(c => c.Employees).Single().UserId);
            Assert.Equal("APPLIED", (await employee.GetFromJsonAsync<ExchangeResponse[]>(ExchangePath))!.Single().Status);
            using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Empty(await db.Set<ShiftExchangeReservation>().ToArrayAsync());
            Assert.Equal(4, await db.Set<ScheduleEditAudit>().CountAsync(a => a.Operation == "EXCHANGE_STATUS"));
            Assert.Equal(JsonSerializer.Serialize(pub.Snapshot), JsonSerializer.Serialize(JsonSerializer.Deserialize<ScheduleSnapshot>((await db.Set<SchedulePublication>().SingleAsync(p => p.Id == pub.Id)).Snapshot)));
        }
    }
    [PostgresFact]
    public async Task Exchanges_enforce_ownership_rejection_cancellation_and_one_concurrent_request_per_assignment()
    {
        var (admin, employee, replacement, pub) = await ExchangeSetup(false); using (admin) using (employee) using (replacement)
        {
            var assignment = pub.Snapshot!.Cells.SelectMany(c => c.Employees).Single();
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(replacement, ExchangePath + "/2026/10", new ExchangeCreateRequest(pub.Id, assignment.AssignmentId, ids["employee"]), "POST")).StatusCode);
            using var second = await Login("employee");
            var payload = new ExchangeCreateRequest(pub.Id, assignment.AssignmentId, ids["coordinator"]);
            var responses = await Task.WhenAll(Send(employee, ExchangePath + "/2026/10",payload,"POST"), Send(second, ExchangePath + "/2026/10",payload,"POST"));
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created); Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
            var request = (await employee.GetFromJsonAsync<ExchangeResponse[]>(ExchangePath))!.Single();
            request = await ExchangeDecision(replacement,request,"REJECT"); Assert.Equal("REJECTED",request.Status);
            request = await RequestExchange(employee,pub); request = await ExchangeDecision(employee,request,"CANCEL"); Assert.Equal("CANCELLED",request.Status);
            request = await RequestExchange(employee,pub); await Publish(admin,await Review(admin),"FINAL");
            Assert.Equal("SUPERSEDED",(await employee.GetFromJsonAsync<ExchangeResponse[]>(ExchangePath))!.Single(r=>r.Id==request.Id).Status);
        }
    }
    [PostgresTheory]
    [InlineData("ABSENCE", "APPROVED_ABSENCE")]
    [InlineData("BLOCK", "HARD_BLOCK_CONFLICT")]
    [InlineData("OVERLAP", "WORK_OVERLAP_OR_CONSECUTIVE")]
    [InlineData("CONSECUTIVE", "WORK_OVERLAP_OR_CONSECUTIVE")]
    [InlineData("QUALIFICATION", "QUALIFICATION_REQUIRED")]
    [InlineData("WEEKENDS", "WEEKEND_ALLOCATION_LIMIT")]
    [InlineData("NIGHTS", "NIGHT_WEEKLY_LIMIT")]
    [InlineData("SIX_NINE", "SIX_NINE_WEEKLY_LIMIT")]
    public async Task Exchange_approval_revalidates_hard_constraints_after_consent_and_rolls_back(string change,string code)
    {
        var (admin, employee, replacement, pub) = await ExchangeSetup(hardCase: change); using(admin) using(employee) using(replacement)
        {
            var request = await RequestExchange(employee,pub); request = await ExchangeDecision(replacement,request,"ACCEPT");
            using(var scope=factory.Services.CreateScope())
            {
                var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var source=await db.Set<ShiftAssignment>().Include(a=>a.Cell).Include(a=>a.Shift).SingleAsync();
                var definition=await db.Set<ShiftTypeDefinition>().SingleAsync(d=>d.Id==source.Cell!.ShiftTypeDefinitionId);
                if(change=="ABSENCE") db.Add(new AbsenceRequest {UserId=ids["coordinator"], Reason="VACATION",Status="APPROVED",StartDate=new(2026,10,1),EndDate=new(2026,10,1),FullDay=true,CreatedAt=clock.Now.UtcDateTime,UpdatedAt=clock.Now.UtcDateTime});
                if(change=="BLOCK") db.Add(new ShiftPreference {UserId=ids["coordinator"],ShiftId=source.ShiftId,PreferenceType=PreferenceType.BLOCKED});
                if(change=="QUALIFICATION") definition.RequiresShiftLeadQualification=true;
                var dates=change=="SIX_NINE" ? new[]{new DateOnly(2026,9,29)} : change=="WEEKENDS" ? new[]{new DateOnly(2026,10,9),new DateOnly(2026,10,16)} : change=="NIGHTS" ? new[]{new DateOnly(2026,9,27),new DateOnly(2026,9,29)} : new[]{change=="CONSECUTIVE"?new DateOnly(2026,9,30):new DateOnly(2026,10,1)};
                if(change is "OVERLAP" or "CONSECUTIVE" or "NIGHTS" or "SIX_NINE" or "WEEKENDS")
                foreach(var date in dates)
                {
                    var period=db.Set<SchedulePeriod>().Local.SingleOrDefault(p=>p.Year==date.Year&&p.Month==date.Month) ?? await db.Set<SchedulePeriod>().SingleOrDefaultAsync(p=>p.Year==date.Year&&p.Month==date.Month);
                    if(period is null){period=new SchedulePeriod{Year=date.Year,Month=date.Month};db.Add(period);}
                    var type=change is "CONSECUTIVE" or "NIGHTS" ? ShiftType.NIGHT:ShiftType.DAY;
                    var time=ShiftClock.Boundaries(date,type);
                    var shift=await db.Set<Shift>().SingleOrDefaultAsync(s=>s.ShiftDate==date&&s.ShiftType==type);
                    if(shift is null){shift=new Shift{SchedulePeriod=period,ShiftDate=date,ShiftType=type,StartsAt=time.StartsAt,EndsAt=time.EndsAt};db.Add(shift);}
                    SchedulingCell? cell=null;
                    if(change is "SIX_NINE" or "WEEKENDS") {cell=new SchedulingCell{Date=date,SchedulePeriod=period,ShiftTypeDefinitionId=definition.Id,StartsAt=time.StartsAt,EndsAt=time.EndsAt,RequiredEmployees=1,ConfigurationVersion=1, SchedulingRunId=source.Cell!.SchedulingRunId};db.Add(cell);}
                    db.Add(new ShiftAssignment{UserId=ids["coordinator"],Shift=shift,Cell=cell});
                }
                await db.SaveChangesAsync();
            }
            var response=await Send(admin,$"/api/admin/shift-exchanges/{request.Id}/decision",new ExchangeDecisionRequest(request.Version,"APPROVE"),"POST");
            Assert.Equal(HttpStatusCode.Conflict,response.StatusCode); Assert.Equal(code,(await response.Content.ReadFromJsonAsync<ApiError>())!.Code);
            Assert.Contains((await Review(admin)).Draft.Cells.SelectMany(c=>c.Employees),a=>a.UserId==ids["employee"]);
            Assert.Equal("AWAITING_ADMIN",(await employee.GetFromJsonAsync<ExchangeResponse[]>(ExchangePath))!.Single().Status);
        }
    }
    [PostgresFact]
    public async Task Hidden_publication_details_are_redacted_from_employee_absence_conflicts_and_require_admin_for_team_approvers()
    {
        var (admin,employee,replacement,pub)=await ExchangeSetup(); using(admin) using(employee) using(replacement)
        {
            await SetupTeam(admin);
            var request=await SubmitAbsence(employee);
            await DecideAbsence(admin,request);
            Assert.NotEmpty((await employee.GetFromJsonAsync<AbsenceResponse[]>("/api/absences/mine"))!.Single().UnresolvedConflicts);
            Assert.Equal(HttpStatusCode.NoContent,(await Send(admin,ControlPath(pub.Id),new PublicationControlRequest(0,"HIDE","בדיקה"),"POST")).StatusCode);
            Assert.Empty((await employee.GetFromJsonAsync<AbsenceResponse[]>("/api/absences/mine"))!.Single().UnresolvedConflicts);
            using var leader=await Login("leader");
            Assert.Equal(HttpStatusCode.Conflict,(await leader.GetAsync($"/api/absences/{request.Id}/conflicts")).StatusCode);
            Assert.Equal(HttpStatusCode.OK,(await admin.GetAsync($"/api/absences/{request.Id}/conflicts")).StatusCode);
        }
    }
    [PostgresFact]
    public async Task Dedicated_first_team_leader_can_approve_own_team_but_cannot_manage_another_team_or_administration()
    {
        using var admin=await Login();await SetupTeam(admin);await ConfigureWindow(admin);
        using var leader=await Login("leader"); using var employee=await Login("employee");
        var teams=(await leader.GetFromJsonAsync<TeamResponse[]>("/api/team-availability/members"))!;
        Assert.Single(teams);Assert.Equal(ids["leader"],teams[0].LeaderUserId);
        Assert.Equal(HttpStatusCode.Created,(await Send(leader,Delegated(ids["employee"]),Choices(),"POST")).StatusCode);
        var absence=await SubmitAbsence(employee);Assert.Equal("APPROVED",(await DecideAbsence(leader,absence)).Status);
        Assert.Equal(HttpStatusCode.Forbidden,(await leader.GetAsync(ReviewPath)).StatusCode);
        var other=(await Teams(admin))[1];
        await Send(admin,$"{TeamsPath}/members/{ids["employee"]}",new MembershipRequest(other.Id));
        Assert.Equal(HttpStatusCode.Forbidden,(await leader.GetAsync(Delegated(ids["employee"]))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await leader.GetAsync($"/api/absences/{absence.Id}/conflicts")).StatusCode);
    }

    [PostgresFact]
    public async Task Concurrent_approvals_for_different_sources_cannot_assign_the_same_replacement_twice()
    {
        var (admin,employee,secondRequester,pub)=await ExchangeSetup(); using(admin) using(employee) using(secondRequester)
        {
            Guid replacementId;var original=pub.Snapshot!.Cells.Single(c=>c.Employees.Length>0);
            using(var scope=factory.Services.CreateScope())
            {
                var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var user=new ApplicationUser{UserName="replacement",FirstName="עובד",LastName="מחליף",IsActive=true};
                Assert.True((await users.CreateAsync(user,Password)).Succeeded);await users.AddToRoleAsync(user,"EMPLOYEE");replacementId=user.Id;
                var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var d=await db.Set<ShiftTypeDefinition>().SingleAsync(d=>d.Id==original.DefinitionId);d.RequiredEmployees=2;d.Version++;await db.SaveChangesAsync();
            }
            var r=await Review(admin);r=await Edit(admin,r,"ASSIGN",ids["coordinator"],original.DefinitionId,1);r=await Ack(admin,r);pub=await Publish(admin,r,"FINAL");
            using var replacement=await Login("replacement");
            async Task<ExchangeResponse> CreateFor(HttpClient requester,Guid user)
            {
                var assignment=pub.Snapshot!.Cells.SelectMany(c=>c.Employees).Single(e=>e.UserId==user);
                var response=await Send(requester,ExchangePath+"/2026/10",new ExchangeCreateRequest(pub.Id,assignment.AssignmentId,replacementId),"POST");
                Assert.Equal(HttpStatusCode.Created,response.StatusCode);return (await response.Content.ReadFromJsonAsync<ExchangeResponse>())!;
            }
            var first=await CreateFor(employee,ids["employee"]);var second=await CreateFor(secondRequester,ids["coordinator"]);
            first=await ExchangeDecision(replacement,first,"ACCEPT");second=await ExchangeDecision(replacement,second,"ACCEPT");
            using var anotherAdmin=await Login();
            var writes=await Task.WhenAll(Send(admin,$"/api/admin/shift-exchanges/{first.Id}/decision",new ExchangeDecisionRequest(first.Version,"APPROVE"),"POST"),
                Send(anotherAdmin,$"/api/admin/shift-exchanges/{second.Id}/decision",new ExchangeDecisionRequest(second.Version,"APPROVE"),"POST"));
            Assert.Single(writes,w=>w.StatusCode==HttpStatusCode.OK);Assert.Single(writes,w=>w.StatusCode==HttpStatusCode.Conflict);
            Assert.Single((await Review(admin)).Draft.Cells.SelectMany(c=>c.Employees),e=>e.UserId==replacementId);
            Assert.Equal(2,(await Review(admin)).Draft.Cells.SelectMany(c=>c.Employees).Count());
        }
    }

}
