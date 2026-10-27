using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ShiftManager.Api;
using ShiftManager.Data;
using ShiftManager.Domain;
namespace ShiftManager.Tests;
public sealed partial class SchedulingTests
{
    private const string ReviewPath = "/api/admin/scheduling/periods/2026/10";
    private static async Task<ReviewResponse> Review(HttpClient admin) => (await admin.GetFromJsonAsync<ReviewResponse>(ReviewPath))!;
    private async Task<(HttpClient Admin, Guid Definition, ReviewResponse Review)> ManualSetup()
    {
        var admin = await Login(); var definitions = await ConfigureAutomatic(1);
        return (admin, definitions[0], await Review(admin));
    }
    private static async Task<ReviewResponse> Edit(HttpClient admin, ReviewResponse r, string operation, Guid? employee, Guid? definition, int day, Guid? assignment = null)
    {
        var response = await Send(admin, ReviewPath + "/assignments", new ManualAssignmentRequest(r.Token, operation, assignment, employee, definition, new(2026,10,day)), "POST");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); return (await response.Content.ReadFromJsonAsync<ReviewResponse>())!;
    }
    private static async Task<ReviewResponse> Ack(HttpClient admin, ReviewResponse r)
    {
        var response = await Send(admin, ReviewPath + "/review", new ReviewTokenRequest(r.Token), "POST");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); return (await response.Content.ReadFromJsonAsync<ReviewResponse>())!;
    }
    private static async Task<PublicationResponse> Publish(HttpClient admin, ReviewResponse r, string type, bool shortages = true, bool issues = true)
    {
        var response = await Send(admin, ReviewPath + "/publications/" + type, new PublishScheduleRequest(r.Token, shortages, issues, "הערה לפרסום"), "POST");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); return (await response.Content.ReadFromJsonAsync<PublicationResponse>())!;
    }
    [PostgresFact]
    public async Task Monthly_board_reuses_all_51_rows_and_actual_month_boundaries()
    {
        using var admin=await Login();
        var board=(await admin.GetFromJsonAsync<WeeklyBoardResponse>("/api/admin/scheduling/board?date=2026-10-20&monthly=true"))!;
        Assert.Equal(new DateOnly(2026,10,1),board.WeekStartsOn);Assert.Equal(new DateOnly(2026,10,31),board.WeekEndsOn);
        Assert.Equal(31,board.Days.Length);Assert.Equal(51,board.Rows.Length);Assert.All(board.Rows,r=>Assert.Equal(31,r.Cells.Length));
    }
    [PostgresFact]
    public async Task Publication_lifecycle_snapshots_correction_and_final_are_sequential_and_immutable()
    {
        var (admin, definition, r) = await ManualSetup(); using (admin)
        {
            r = await Edit(admin, r, "ASSIGN", ids["employee"], definition, 1);
            Assert.True(r.NeedsReview); r = await Ack(admin, r); Assert.True(r.PublicationReady);
            var v1 = await Publish(admin, r, "PRELIMINARY"); Assert.Equal(1, v1.VersionNumber);
            using var employee = await Login("employee");
            var visible = (await employee.GetFromJsonAsync<PublicationResponse>("/api/my-schedule/2026/10"))!; Assert.Equal(v1.Id, visible.Id);
            r = await Review(admin);
            var correction = await Send(admin, ReviewPath + "/correction", new ReviewTokenRequest(r.Token), "POST"); Assert.Equal(HttpStatusCode.OK, correction.StatusCode);
            r = (await correction.Content.ReadFromJsonAsync<ReviewResponse>())!;
            r = await Edit(admin, r, "MOVE", null, definition, 2, v1.Snapshot!.Cells.SelectMany(c => c.Employees).Single().AssignmentId);
            var during = (await employee.GetFromJsonAsync<PublicationResponse>("/api/my-schedule/2026/10"))!;
            Assert.Equal(new DateOnly(2026,10,1), during.Snapshot!.Cells.Single(c => c.Employees.Length > 0).Date);
            var v2 = await Publish(admin, r, "PRELIMINARY"); Assert.Equal(2, v2.VersionNumber);
            var v3 = await Publish(admin, await Review(admin), "FINAL"); Assert.Equal(3, v3.VersionNumber);
            var final = (await employee.GetFromJsonAsync<PublicationResponse>("/api/my-schedule/2026/10"))!; Assert.Equal("FINAL", final.PublicationType);
            var history = (await admin.GetFromJsonAsync<PublicationResponse[]>(ReviewPath + "/publications"))!;
            Assert.Equal(new[] {3,2,1}, history.Select(h => h.VersionNumber));
            Assert.Equal(new DateOnly(2026,10,1), history[2].Snapshot!.Cells.Single(c => c.Employees.Length > 0).Date);
            using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var update = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"SchedulePublication\" SET \"Notes\" = 'changed' WHERE \"Id\" = {v1.Id}")); Assert.Equal("23514", update.SqlState);
            var deletion = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"SchedulePublication\" WHERE \"Id\" = {v1.Id}")); Assert.Equal("23514", deletion.SqlState);
        }
    }
    [PostgresFact]
    public async Task Invalid_transitions_stale_tokens_and_missing_shortage_acknowledgment_are_rejected()
    {
        var (admin, definition, r) = await ManualSetup(); using (admin)
        {
            r = await Edit(admin,r,"ASSIGN",ids["employee"],definition,1); r = await Ack(admin,r);
            Assert.Equal(HttpStatusCode.Conflict,(await Send(admin,ReviewPath+"/publications/FINAL",new PublishScheduleRequest(r.Token,true,true,null),"POST")).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict,(await Send(admin,ReviewPath+"/correction",new ReviewTokenRequest(r.Token),"POST")).StatusCode);
            var shortage = await Send(admin,ReviewPath+"/publications/PRELIMINARY",new PublishScheduleRequest(r.Token,false,true,null),"POST");
            Assert.Equal("SHORTAGES_REQUIRE_ACKNOWLEDGMENT",(await shortage.Content.ReadFromJsonAsync<ApiError>())!.Code);
            var oldToken = r.Token; await Publish(admin,r,"PRELIMINARY");
            Assert.Equal(HttpStatusCode.Conflict,(await Send(admin,ReviewPath+"/publications/PRELIMINARY",new PublishScheduleRequest(oldToken,true,true,null),"POST")).StatusCode);
            var published = await Review(admin);
            Assert.Equal(HttpStatusCode.Conflict,(await Send(admin,ReviewPath+"/assignments",new ManualAssignmentRequest(published.Token,"ASSIGN",null,ids["employee"],definition,new(2026,10,3)),"POST")).StatusCode);
            await Publish(admin,published,"FINAL");
            Assert.Equal(HttpStatusCode.Conflict,(await Send(admin,ReviewPath+"/correction",new ReviewTokenRequest((await Review(admin)).Token),"POST")).StatusCode);
        }
    }
    [PostgresFact]
    public async Task Manual_assign_replace_move_remove_are_audited_and_concurrency_safe()
    {
        var (admin,d,r) = await ManualSetup(); using(admin)
        {
            var old = r.Token; r = await Edit(admin,r,"ASSIGN",ids["employee"],d,1);
            Assert.Equal(HttpStatusCode.Conflict,(await Send(admin,ReviewPath+"/assignments",new ManualAssignmentRequest(old,"ASSIGN",null,ids["employee"],d,new(2026,10,2)),"POST")).StatusCode);
            var a = r.Draft.Cells.SelectMany(c=>c.Employees).Single(); r = await Edit(admin,r,"REPLACE",ids["employee"],d,1,a.AssignmentId);
            a = r.Draft.Cells.SelectMany(c=>c.Employees).Single(); r = await Edit(admin,r,"MOVE",null,d,2,a.AssignmentId);
            a = r.Draft.Cells.SelectMany(c=>c.Employees).Single(); r = await Edit(admin,r,"REMOVE",null,null,2,a.AssignmentId);
            Assert.Empty(r.Draft.Cells.SelectMany(c=>c.Employees));
            var audit = (await admin.GetFromJsonAsync<ScheduleEditAudit[]>(ReviewPath+"/audit"))!; Assert.Equal(4,audit.Length);
            Assert.All(audit,x=>Assert.Equal(ids["admin"],x.ActorUserId));
            Assert.Contains(audit,x=>x.Operation=="MOVE" && x.Before!="null" && x.After!="null");
        }
    }
    [PostgresFact]
    public async Task Manual_constraints_blocks_overlap_consecutive_expert_leaders_and_capacity_are_enforced()
    {
        var (admin,d,r) = await ManualSetup(); using(admin)
        {
            await ConfigureWindow(admin,open:true);
            using var employee = await Login("employee"); await Send(employee,"/api/availability/2026/10",Choices(0,1),"POST"); r=await Review(admin);
            async Task Expect(string code,int day, Guid? row=null)
            {
                var response=await Send(admin,ReviewPath+"/assignments",new ManualAssignmentRequest((await Review(admin)).Token,"ASSIGN",null,ids["employee"],row??d,new(2026,10,day)),"POST");
                Assert.Equal(HttpStatusCode.Conflict,response.StatusCode); Assert.Equal(code,(await response.Content.ReadFromJsonAsync<ApiError>())!.Code);
            }
            await Expect("HARD_BLOCK_CONFLICT",1); r=await Edit(admin,r,"ASSIGN",ids["employee"],d,2);
            await Expect("STAFFING_EXCEEDED",2);
            var defs=await Types(admin);var request=Request(defs);request.ShiftTypes[1]=request.ShiftTypes[1] with {RequiredEmployees=1,Period="NIGHT",TimeRange="NIGHT"}; await Send(admin,TypesPath,request);
            await Expect("WORK_OVERLAP_OR_CONSECUTIVE",2,defs[1].Id);
            request=Request(await Types(admin));request.ShiftTypes[1]=request.ShiftTypes[1] with {RequiredEmployees=1,RequiresShiftLeadQualification=true};await Send(admin,TypesPath,request);
            await Expect("QUALIFICATION_REQUIRED",4,defs[1].Id);
            var team=await SetupTeam(admin); await Send(admin,$"{TeamsPath}/{team.Id}",new UpdateTeamRequest(team.Name,ids["employee"],ids["coordinator"],team.Version));
            await Expect("TEAM_LEADER_ASSIGNED",4);
        }
    }
    [PostgresFact]
    public async Task Failed_replacement_rolls_back_removal_and_audit_atomically()
    {
        var (admin,d,r)=await ManualSetup();using(admin)
        {
            r=await Edit(admin,r,"ASSIGN",ids["employee"],d,1);var assignment=r.Draft.Cells.SelectMany(c=>c.Employees).Single();
            var response=await Send(admin,ReviewPath+"/assignments",new ManualAssignmentRequest(r.Token,"REPLACE",assignment.AssignmentId,ids["coordinator"],d,new(2026,10,1)),"POST");
            Assert.Equal(HttpStatusCode.Conflict,response.StatusCode);
            var after=await Review(admin);Assert.Equal(assignment.AssignmentId,after.Draft.Cells.SelectMany(c=>c.Employees).Single().AssignmentId);Assert.Equal(r.Token,after.Token);
            Assert.Single((await admin.GetFromJsonAsync<ScheduleEditAudit[]>(ReviewPath+"/audit"))!);
        }
    }
    [PostgresFact]
    public async Task Publication_revalidates_expert_requirements_even_after_previous_review()
    {
        var (admin,d,r)=await ManualSetup();using(admin)
        {
            r=await Edit(admin,r,"ASSIGN",ids["employee"],d,1);r=await Ack(admin,r);
            var request=Request(await Types(admin));request.ShiftTypes[0]=request.ShiftTypes[0] with {RequiresShiftLeadQualification=true};await Send(admin,TypesPath,request);
            var stale=await Review(admin);Assert.True(stale.NeedsReview);Assert.Contains(stale.Conflicts,c=>c.Code=="QUALIFICATION_REQUIRED");
            var response=await Send(admin,ReviewPath+"/publications/PRELIMINARY",new PublishScheduleRequest(stale.Token,true,true,null),"POST");Assert.Equal(HttpStatusCode.Conflict,response.StatusCode);
            Assert.Empty((await admin.GetFromJsonAsync<PublicationResponse[]>(ReviewPath+"/publications"))!);
        }
    }
    [PostgresFact]
    public async Task Manual_candidates_explain_ineligibility_and_deputies_remain_eligible()
    {
        var (admin,d,r)=await ManualSetup();using(admin)
        {
            await SetupTeam(admin);
            var candidates=(await admin.GetFromJsonAsync<CandidateResponse[]>($"{ReviewPath}/candidates?definitionId={d}&date=2026-10-01"))!;
            Assert.Contains(candidates,c=>c.UserId==ids["coordinator"]&&c.Eligible);
            Assert.Contains(candidates,c=>c.UserId==ids["admin"]&&!c.Eligible&&c.Reasons.Contains("TEAM_LEADER_ASSIGNED"));
        }
    }
    [PostgresFact]
    public async Task Manual_cross_month_and_previous_night_constraints_are_enforced()
    {
        var (admin,d,r)=await ManualSetup(); using(admin)
        {
            using(var scope=factory.Services.CreateScope())
            {
                var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var p=new SchedulePeriod{Year=2026,Month=9};var t=ShiftClock.Boundaries(new(2026,9,30),ShiftType.NIGHT);
                var shift=new Shift{SchedulePeriod=p,ShiftDate=new(2026,9,30),ShiftType=ShiftType.NIGHT,StartsAt=t.StartsAt,EndsAt=t.EndsAt};db.Add(new ShiftAssignment{Shift=shift,UserId=ids["employee"]});await db.SaveChangesAsync();
            }
            r=await Review(admin);var blocked=await Send(admin,ReviewPath+"/assignments",new ManualAssignmentRequest(r.Token,"ASSIGN",null,ids["employee"],d,new(2026,10,1)),"POST");
            Assert.Equal("WORK_OVERLAP_OR_CONSECUTIVE",(await blocked.Content.ReadFromJsonAsync<ApiError>())!.Code);
        }
    }
    [PostgresFact]
    public async Task Employee_privacy_issue_ownership_and_administrator_resolution_are_enforced()
    {
        var (admin,d,r)=await ManualSetup();using(admin)
        {
            using var employee=await Login("employee");using var coordinator=await Login("coordinator");
            Assert.Equal(HttpStatusCode.Forbidden,(await employee.GetAsync(ReviewPath)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,(await employee.GetAsync("/api/my-schedule/2026/10")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await coordinator.GetAsync(ReviewPath)).StatusCode);
            await SetupTeam(admin);r=await Review(admin);r=await Edit(admin,r,"ASSIGN",ids["employee"],d,1);r=await Edit(admin,r,"ASSIGN",ids["coordinator"],d,2);r=await Ack(admin,r);
            var pub=await Publish(admin,r,"PRELIMINARY");var assignment=pub.Snapshot!.Cells.SelectMany(c=>c.Employees).Single(e=>e.UserId==ids["employee"]);
            using var deputy=await Login("coordinator");
            var privateView=(await deputy.GetFromJsonAsync<PublicationResponse>("/api/my-schedule/2026/10"))!;
            Assert.DoesNotContain(privateView.Snapshot!.Cells.SelectMany(c=>c.Employees),e=>e.UserId==ids["employee"]);
            Assert.Equal(HttpStatusCode.Forbidden,(await Send(deputy,"/api/my-schedule/2026/10/issues",new CreateIssueRequest(pub.Id,assignment.AssignmentId,"בעיה"),"POST")).StatusCode);
            var report=await Send(employee,"/api/my-schedule/2026/10/issues",new CreateIssueRequest(pub.Id,assignment.AssignmentId,"שיבוץ לא צפוי לבדיקה"),"POST");Assert.Equal(HttpStatusCode.Created,report.StatusCode);
            var issue=(await report.Content.ReadFromJsonAsync<ScheduleIssue>())!;
            Assert.Empty((await deputy.GetFromJsonAsync<ScheduleIssue[]>("/api/my-schedule/2026/10/issues"))!);
            r=await Review(admin);var finalBlocked=await Send(admin,ReviewPath+"/publications/FINAL",new PublishScheduleRequest(r.Token,true,false,null),"POST");
            Assert.Equal("ISSUES_REQUIRE_ACKNOWLEDGMENT",(await finalBlocked.Content.ReadFromJsonAsync<ApiError>())!.Code);
            Assert.Equal(HttpStatusCode.OK,(await Send(admin,$"{ReviewPath}/issues/{issue.Id}",new UpdateIssueRequest(issue.Version,"RESOLVED","נבדק מול העובד"))).StatusCode);
            var updated=(await employee.GetFromJsonAsync<ScheduleIssue[]>("/api/my-schedule/2026/10/issues"))!.Single();Assert.Equal("RESOLVED",updated.Status);
            await Publish(admin,await Review(admin),"FINAL");
            Assert.Equal(HttpStatusCode.Conflict,(await Send(employee,"/api/my-schedule/2026/10/issues",new CreateIssueRequest(pub.Id,assignment.AssignmentId,"מאוחר"),"POST")).StatusCode);
        }
    }
    [PostgresFact]
    public async Task Delegated_availability_changes_stale_review_without_mutating_preliminary_snapshot()
    {
        var (admin,d,r)=await ManualSetup();using(admin)
        {
            await SetupTeam(admin); await ConfigureWindow(admin);r=await Review(admin);r=await Edit(admin,r,"ASSIGN",ids["employee"],d,1);r=await Ack(admin,r);
            var pub=await Publish(admin,r,"PRELIMINARY");using var deputy=await Login("coordinator");
            Assert.Equal(HttpStatusCode.Created,(await Send(deputy,Delegated(ids["employee"]),Choices(0,1),"POST")).StatusCode);
            r=await Review(admin);Assert.True(r.NeedsReview);Assert.Contains(r.Conflicts,c=>c.Code=="HARD_BLOCK_CONFLICT");
            var blocked=await Send(admin,ReviewPath+"/publications/FINAL",new PublishScheduleRequest(r.Token,true,true,null),"POST");Assert.Equal(HttpStatusCode.Conflict,blocked.StatusCode);
            using var employee=await Login("employee");Assert.Equal(pub.Id,(await employee.GetFromJsonAsync<PublicationResponse>("/api/my-schedule/2026/10"))!.Id);
        }
    }
    [PostgresFact]
    public async Task Concurrent_publication_has_one_winner_and_history_survives_profile_and_definition_changes()
    {
        var (admin,d,r)=await ManualSetup();using(admin)
        {
            r=await Edit(admin,r,"ASSIGN",ids["employee"],d,1);r=await Ack(admin,r);using var second=await Login();
            var request=new PublishScheduleRequest(r.Token,true,true,null);var responses=await Task.WhenAll(Send(admin,ReviewPath+"/publications/PRELIMINARY",request,"POST"),Send(second,ReviewPath+"/publications/PRELIMINARY",request,"POST"));
            Assert.Single(responses,x=>x.StatusCode==HttpStatusCode.OK);Assert.Single(responses,x=>x.StatusCode==HttpStatusCode.Conflict);
            var initial=(await admin.GetFromJsonAsync<PublicationResponse[]>(ReviewPath+"/publications"))!.Single();var name=initial.Snapshot!.Cells[0].Name;var employeeName=initial.Snapshot.Cells.SelectMany(c=>c.Employees).Single().Name;
            var definitions=await Types(admin);var change=Request(definitions);change.ShiftTypes[0]=change.ShiftTypes[0] with{Name="שם חדש",RequiredEmployees=3};await Send(admin,TypesPath,change);
            await Send(admin,$"/api/users/{ids["employee"]}",new ProfileRequest("שם","חדש","employee"));
            var history=(await admin.GetFromJsonAsync<PublicationResponse[]>(ReviewPath+"/publications"))!.Single();Assert.Equal(name,history.Snapshot!.Cells[0].Name);Assert.Equal(employeeName,history.Snapshot.Cells.SelectMany(c=>c.Employees).Single().Name);Assert.Equal(1,history.Snapshot.Cells[0].RequiredEmployees);
        }
    }
    [PostgresFact]
    public async Task Publication_upgrade_preserves_existing_users_preferences_and_legacy_history()
    {
        using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var migrator=db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261027112256_TeamManagement");
        var period=Guid.NewGuid();var publication=Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"SchedulePeriod\" (\"Id\",\"Year\",\"Month\",\"Status\",\"CreatedAt\",\"UpdatedAt\") VALUES ({period},2026,10,'PUBLISHED',{DateTime.UtcNow},{DateTime.UtcNow})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"SchedulePublication\" (\"Id\",\"SchedulePeriodId\",\"PublishedByUserId\",\"PublishedAt\",\"VersionNumber\") VALUES ({publication},{period},{ids["admin"]},{DateTime.UtcNow},1)");
        await db.Database.MigrateAsync();db.ChangeTracker.Clear();var legacy=await db.Set<SchedulePublication>().SingleAsync();Assert.Equal("LEGACY",legacy.PublicationType);Assert.Equal("null",legacy.Snapshot);Assert.Equal(4,await db.Users.CountAsync());Assert.Equal(4,await db.Set<OrganizationalTeam>().CountAsync());Assert.False(db.Database.HasPendingModelChanges());
    }
}
