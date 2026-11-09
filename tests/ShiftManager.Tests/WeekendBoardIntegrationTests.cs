using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShiftManager.Api;
using ShiftManager.Data;
using ShiftManager.Domain;
using ShiftManager.Services.AutomaticScheduling;
namespace ShiftManager.Tests;

public sealed partial class SchedulingTests
{
    private static Guid BoardDefinition(int slot) => Guid.Parse($"40000000-0000-0000-0000-{slot:000000000000}");
    private async Task<(HttpClient Admin, Guid Definition, ReviewResponse Review)> PairSetup()
    {
        var admin = await Login();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var d = await db.Set<ShiftTypeDefinition>().SingleAsync(d => d.Slot == 16);
        d.Semantic = SchedulingRules.OnCall; d.RequiredEmployees = 1; d.TimeRange = ShiftTimeRange.DAY; d.Period = ShiftType.DAY;
        await db.SaveChangesAsync();
        return (admin, d.Id, await Review(admin));
    }
    private static async Task<ReviewResponse> Note(HttpClient admin, ReviewResponse review, Guid assignment, string? text)
    {
        var response = await Send(admin, ReviewPath + "/assignments", new ManualAssignmentRequest(review.Token, "NOTE", assignment, null, null, null, text), "POST");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ReviewResponse>())!;
    }

    [PostgresFact]
    public async Task Weekend_manual_operations_candidates_board_and_publications_share_authoritative_rule()
    {
        var (admin, allowed, r) = await ManualSetup(); using (admin)
        {
            var disallowed = BoardDefinition(6);
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                (await db.Set<ShiftTypeDefinition>().SingleAsync(d => d.Id == disallowed)).RequiredEmployees = 3;
                await db.SaveChangesAsync();
            }
            r = await Review(admin);
            var candidates = (await admin.GetFromJsonAsync<CandidateResponse[]>($"{ReviewPath}/candidates?definitionId={disallowed}&date=2026-10-02"))!;
            Assert.All(candidates, c => { Assert.False(c.Eligible); Assert.Contains("WEEKEND_SHIFT_PROHIBITED", c.Reasons); });
            var rejected = await Send(admin, ReviewPath + "/assignments", new ManualAssignmentRequest(r.Token, "ASSIGN", null, ids["employee"], disallowed, new(2026,10,2)), "POST");
            Assert.Equal("WEEKEND_SHIFT_PROHIBITED", (await rejected.Content.ReadFromJsonAsync<ApiError>())!.Code);
            r = await Edit(admin, r, "ASSIGN", ids["employee"], allowed, 1);
            var original = r.Draft.Cells.SelectMany(c => c.Employees).Single();
            rejected = await Send(admin, ReviewPath + "/assignments", new ManualAssignmentRequest(r.Token, "MOVE", original.AssignmentId, null, disallowed, new(2026,10,2)), "POST");
            Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
            Assert.Equal(original.AssignmentId, (await Review(admin)).Draft.Cells.SelectMany(c => c.Employees).Single().AssignmentId);
            var board = (await admin.GetFromJsonAsync<WeeklyBoardResponse>("/api/admin/scheduling/board?date=2026-10-02"))!;
            var cell = board.Rows.Single(row => row.Definition.Id == disallowed).Cells.Single(c => c.Date == new DateOnly(2026,10,2));
            Assert.Equal("NOT_APPLICABLE", cell.State); Assert.Equal(0, cell.RequiredEmployees); Assert.Equal(0, cell.Shortage);
            r = await Ack(admin, await Review(admin)); await Publish(admin, r, "PRELIMINARY");
            r = await Review(admin);
            var correction = await Send(admin, ReviewPath + "/correction", new ReviewTokenRequest(r.Token), "POST");
            Assert.Equal(HttpStatusCode.OK, correction.StatusCode);
            // Simulate old persisted invalid data: neither publication stage may accept it.
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var period = await db.Set<SchedulePeriod>().SingleAsync(); var time = ShiftClock.Boundaries(new(2026,10,2), ShiftType.DAY);
                db.Add(new ShiftAssignment { UserId = ids["employee"], Shift = new Shift { SchedulePeriodId = period.Id, ShiftDate = new(2026,10,2), StartsAt = time.StartsAt, EndsAt = time.EndsAt } });
                await db.SaveChangesAsync();
            }
            r = await Review(admin); Assert.Contains(r.Conflicts, c => c.Code == "WEEKEND_SHIFT_PROHIBITED");
            rejected = await Send(admin, ReviewPath + "/publications/PRELIMINARY", new PublishScheduleRequest(r.Token,true,true,null), "POST");
            Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
            rejected = await Send(admin, ReviewPath + "/publications/FINAL", new PublishScheduleRequest(r.Token,true,true,null), "POST");
            Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        }
    }

    [PostgresFact]
    public async Task Normal_Saturday_edit_creates_notes_replaces_and_removes_whole_pair_atomically()
    {
        var (admin, definition, r) = await PairSetup(); using (admin)
        {
            var candidates = (await admin.GetFromJsonAsync<CandidateResponse[]>($"{ReviewPath}/candidates?definitionId={definition}&date=2026-10-03"))!;
            Assert.True(candidates.Single(c => c.UserId == ids["employee"]).Eligible);
            r = await Edit(admin,r,"ASSIGN",ids["employee"],definition,3);
            Assert.Equal(2,r.Draft.Cells.Sum(c => c.Employees.Length)); Assert.Empty(r.Conflicts);
            var a = r.Draft.Cells.Single(c => c.Date == new DateOnly(2026,10,3) && c.DefinitionId == definition).Employees.Single();
            r = await Note(admin,r,a.AssignmentId,"להגיע מוקדם\nהערה גלויה");
            Assert.All(r.Draft.Cells.SelectMany(c => c.Employees), e => Assert.Equal("להגיע מוקדם\nהערה גלויה", e.Note));
            var before = r.Token;
            var invalid = await Send(admin,ReviewPath+"/assignments",new ManualAssignmentRequest(r.Token,"REPLACE",a.AssignmentId,ids["admin"],null,null),"POST");
            Assert.Equal(HttpStatusCode.Conflict,invalid.StatusCode);
            r = await Review(admin); Assert.Equal(before,r.Token); Assert.Equal(2,r.Draft.Cells.Sum(c => c.Employees.Length));
            r = await Edit(admin,r,"REPLACE",ids["employee"],null,3,a.AssignmentId);
            Assert.All(r.Draft.Cells.SelectMany(c => c.Employees),e => Assert.Null(e.Note));
            a = r.Draft.Cells.SelectMany(c => c.Employees).First();
            r = await Edit(admin,r,"REMOVE",null,null,2,a.AssignmentId);
            Assert.Empty(r.Draft.Cells.SelectMany(c => c.Employees));
        }
    }

    [PostgresFact]
    public async Task Pair_candidate_checks_blocked_Saturday_and_failed_regular_assignment_rolls_back_both_days()
    {
        var (admin,definition,r) = await PairSetup(); using (admin)
        {
            await ConfigureWindow(admin,open:true); await SetupTeam(admin); using var deputy = await Login("coordinator");
            Assert.Equal(HttpStatusCode.Created, (await Send(deputy,Delegated(ids["employee"]),Choices(0,3),"POST")).StatusCode); r = await Review(admin);
            var candidates = (await admin.GetFromJsonAsync<CandidateResponse[]>($"{ReviewPath}/candidates?definitionId={definition}&date=2026-10-02"))!;
            var candidate = candidates.Single(c => c.UserId == ids["employee"]);
            Assert.False(candidate.Eligible); Assert.Contains("HARD_BLOCK_CONFLICT",candidate.Reasons);
            var invalid = await Send(admin,ReviewPath+"/assignments",new ManualAssignmentRequest(r.Token,"ASSIGN",null,ids["employee"],definition,new(2026,10,2)),"POST");
            Assert.Equal("HARD_BLOCK_CONFLICT",(await invalid.Content.ReadFromJsonAsync<ApiError>())!.Code);
            Assert.Empty((await Review(admin)).Draft.Cells.SelectMany(c => c.Employees));
        }
    }

    [PostgresFact]
    public async Task Saturday_in_next_month_automatically_edits_pair_with_both_periods_locked()
    {
        var (admin,definition,_) = await PairSetup(); using (admin)
        {
            const string may = "/api/admin/scheduling/periods/2027/5";
            var r = (await admin.GetFromJsonAsync<ReviewResponse>(may))!;
            var response = await Send(admin,may+"/assignments",new ManualAssignmentRequest(r.Token,"ASSIGN",null,ids["employee"],definition,new(2027,5,1)),"POST");
            Assert.Equal(HttpStatusCode.OK,response.StatusCode);
            var april = (await admin.GetFromJsonAsync<ReviewResponse>("/api/admin/scheduling/periods/2027/4"))!;
            r = (await admin.GetFromJsonAsync<ReviewResponse>(may))!;
            Assert.Single(april.Draft.Cells.SelectMany(c=>c.Employees)); Assert.Single(r.Draft.Cells.SelectMany(c=>c.Employees));
            var assignment = r.Draft.Cells.SelectMany(c=>c.Employees).Single();
            response = await Send(admin,may+"/assignments",new ManualAssignmentRequest(r.Token,"REMOVE",assignment.AssignmentId,null,null,null),"POST");
            Assert.Equal(HttpStatusCode.OK,response.StatusCode);
            Assert.Empty((await admin.GetFromJsonAsync<ReviewResponse>("/api/admin/scheduling/periods/2027/4"))!.Draft.Cells.SelectMany(c=>c.Employees));
        }
    }

    [PostgresFact]
    public async Task Notes_are_assignment_scoped_audited_validated_frozen_and_visible_to_every_employee()
    {
        var (admin,definition,r) = await ManualSetup(); using (admin)
        {
            r = await Edit(admin,r,"ASSIGN",ids["employee"],definition,1);
            var a = r.Draft.Cells.SelectMany(c=>c.Employees).Single();
            r = await Note(admin,r,a.AssignmentId,"<script>alert(1)</script>\nלהגיע מוקדם");
            using var employee = await Login("employee");
            await SetupTeam(admin); using var other = await Login("coordinator"); r = await Review(admin);
            var denied = await Send(employee,ReviewPath+"/assignments",new ManualAssignmentRequest(r.Token,"NOTE",a.AssignmentId,null,null,null,"שינוי"),"POST");
            Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
            var oversized = await Send(admin,ReviewPath+"/assignments",new ManualAssignmentRequest(r.Token,"NOTE",a.AssignmentId,null,null,null,new string('א',1001)),"POST");
            Assert.Equal(HttpStatusCode.BadRequest,oversized.StatusCode);
            r = await Ack(admin,r); var v1 = await Publish(admin,r,"PRELIMINARY");
            var published = (await other.GetFromJsonAsync<PublicationResponse>("/api/my-schedule/2026/10"))!;
            Assert.Equal(51*31,published.Snapshot!.Cells.Length);
            Assert.Equal("<script>alert(1)</script>\nלהגיע מוקדם",published.Snapshot.Cells.SelectMany(c=>c.Employees).Single().Note);
            var correction = await Send(admin,ReviewPath+"/correction",new ReviewTokenRequest((await Review(admin)).Token),"POST");
            r = (await correction.Content.ReadFromJsonAsync<ReviewResponse>())!;
            r = await Note(admin,r,a.AssignmentId,"גרסה חדשה");
            Assert.Equal(v1.Snapshot!.Cells.SelectMany(c=>c.Employees).Single().Note,(await other.GetFromJsonAsync<PublicationResponse>("/api/my-schedule/2026/10"))!.Snapshot!.Cells.SelectMany(c=>c.Employees).Single().Note);
            r = await Edit(admin,r,"MOVE",null,definition,2,a.AssignmentId);
            a = r.Draft.Cells.SelectMany(c=>c.Employees).Single(); Assert.Equal("גרסה חדשה",a.Note);
            r = await Note(admin,r,a.AssignmentId,null); Assert.Null(r.Draft.Cells.SelectMany(c=>c.Employees).Single().Note);
            r = await Ack(admin,r); var v2 = await Publish(admin,r,"PRELIMINARY");
            var historical = (await other.GetFromJsonAsync<PublicationResponse>("/api/my-schedule/2026/10?version=1"))!;
            Assert.Equal(v1.Id,historical.Id); Assert.NotNull(historical.Snapshot!.Cells.SelectMany(c=>c.Employees).Single().Note);
            Assert.Null(v2.Snapshot!.Cells.SelectMany(c=>c.Employees).Single().Note);
            Assert.Equal(HttpStatusCode.Forbidden,(await Send(other,"/api/my-schedule/2026/10/issues",new CreateIssueRequest(v2.Id,a.AssignmentId,"לא שלי"),"POST")).StatusCode);
            var audit = (await admin.GetFromJsonAsync<ScheduleEditAudit[]>(ReviewPath+"/audit"))!;
            Assert.Equal(3,audit.Count(a=>a.Operation=="NOTE"));
        }
    }
    [PostgresFact]
    public async Task Notes_protect_generated_assignments_and_legacy_notes_are_published()
    {
        var (admin, definition, r) = await ManualSetup(); using (admin)
        {
            var run = await Generate(admin);
            Assert.Equal(HttpStatusCode.OK, (await Send(admin, $"{GenerationPath}/{run.Id}/save", new {}, "POST")).StatusCode);
            r = await Review(admin);
            var assignment = r.Draft.Cells.First(c => c.Employees.Length > 0).Employees[0];
            r = await Note(admin, r, assignment.AssignmentId, "לשמור גם בחידוש אוטומטי");
            run = await Generate(admin);
            var retained = run.Cells.SelectMany(c => c.Employees).Single(e => e.AssignmentId == assignment.AssignmentId);
            Assert.True(retained.Protected); Assert.Equal("לשמור גם בחידוש אוטומטי", retained.Note);
            Assert.Equal(HttpStatusCode.OK, (await Send(admin, $"{GenerationPath}/{run.Id}/save", new {}, "POST")).StatusCode);
            r = await Review(admin);
            Assert.Equal("לשמור גם בחידוש אוטומטי", r.Draft.Cells.SelectMany(c => c.Employees).Single(e => e.AssignmentId == assignment.AssignmentId).Note);
            // A pre-board allocation has no definition but its note is still frozen and visible.
            await SetupTeam(admin);
            Guid legacy;
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var period = await db.Set<SchedulePeriod>().SingleAsync(); var date = new DateOnly(2026,10,4);
                var time = ShiftClock.Boundaries(date, ShiftType.NIGHT);
                var a = new ShiftAssignment { UserId = ids["coordinator"], Shift = new Shift { SchedulePeriodId = period.Id,
                    ShiftDate = date, ShiftType = ShiftType.NIGHT, StartsAt = time.StartsAt, EndsAt = time.EndsAt } };
                db.Add(a); await db.SaveChangesAsync(); legacy = a.Id;
            }
            r = await Note(admin, await Review(admin), legacy, "הערה לשיבוץ קודם");
            Assert.Equal("הערה לשיבוץ קודם", r.Draft.LegacyAssignments.Single().Note);
            r = await Ack(admin, r); await Publish(admin, r, "PRELIMINARY");
            using var viewer = await Login("employee");
            var visible = (await viewer.GetFromJsonAsync<PublicationResponse>("/api/my-schedule/2026/10"))!;
            Assert.Equal("הערה לשיבוץ קודם", visible.Snapshot!.LegacyAssignments.Single().Note);
        }
    }

}
