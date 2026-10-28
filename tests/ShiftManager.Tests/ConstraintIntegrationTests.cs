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
    private async Task Semantic(Guid id,string semantic)
    {
        using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var d=await db.Set<ShiftTypeDefinition>().SingleAsync(d=>d.Id==id);d.Semantic=semantic;await db.SaveChangesAsync();
    }
    private static async Task<HttpResponseMessage> Pair(HttpClient admin,ReviewResponse r,string operation,DateOnly friday,Guid? user,Guid? assignment=null,bool adjacent=false) =>
        await Send(admin,ReviewPath+"/on-call-pair",new OnCallPairRequest(r.Token,operation,friday,user,assignment,adjacent),"POST");
    [PostgresFact]
    public async Task Constraint_manual_weekend_budget_survives_separate_requests_and_removal()
    {
        var (admin,d,r)=await ManualSetup();using(admin)
        {
            r=await Edit(admin,r,"ASSIGN",ids["employee"],d,2);r=await Edit(admin,r,"ASSIGN",ids["employee"],d,9);
            var third=await Send(admin,ReviewPath+"/assignments",new ManualAssignmentRequest(r.Token,"ASSIGN",null,ids["employee"],d,new(2026,10,16)),"POST");
            Assert.Equal("WEEKEND_ALLOCATION_LIMIT",(await third.Content.ReadFromJsonAsync<ApiError>())!.Code);
            var assignment=r.Draft.Cells.SelectMany(c=>c.Employees).First();r=await Edit(admin,r,"REMOVE",null,null,2,assignment.AssignmentId);r=await Edit(admin,r,"ASSIGN",ids["employee"],d,16);
            Assert.Equal(2,r.Draft.Cells.Sum(c=>c.Employees.Length));
        }
    }
    [PostgresFact]
    public async Task Constraint_six_nine_is_semantic_after_rename_and_rejects_second_weekly_assignment()
    {
        var (admin,d,r)=await ManualSetup();using(admin)
        {
            await Semantic(d,SchedulingRules.SixNine);var request=Request(await Types(admin));request.ShiftTypes[0]=request.ShiftTypes[0] with{Name="שם חופשי"};await Send(admin,TypesPath,request);
            r=await Review(admin);r=await Edit(admin,r,"ASSIGN",ids["employee"],d,1);
            var invalid=await Send(admin,ReviewPath+"/assignments",new ManualAssignmentRequest(r.Token,"ASSIGN",null,ids["employee"],d,new(2026,10,2)),"POST");
            Assert.Equal("SIX_NINE_WEEKLY_LIMIT",(await invalid.Content.ReadFromJsonAsync<ApiError>())!.Code);
            Assert.Equal(SchedulingRules.SixNine,(await Types(admin)).Single(x=>x.Id==d).Semantic);
        }
    }
    [PostgresFact]
    public async Task Constraint_weekly_nights_include_persisted_previous_month_assignments()
    {
        var (admin,d,r)=await ManualSetup();using(admin)
        {
            var request=Request(await Types(admin));request.ShiftTypes[0]=request.ShiftTypes[0] with{Period="NIGHT",TimeRange="NIGHT"};await Send(admin,TypesPath,request);
            using(var scope=factory.Services.CreateScope())
            {
                var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var period=new SchedulePeriod{Year=2026,Month=9};
                foreach(var day in new[]{27,29}){var date=new DateOnly(2026,9,day);var t=ShiftClock.Boundaries(date,ShiftType.NIGHT);db.Add(new ShiftAssignment{UserId=ids["employee"],Shift=new Shift{SchedulePeriod=period,ShiftDate=date,ShiftType=ShiftType.NIGHT,StartsAt=t.StartsAt,EndsAt=t.EndsAt}});}await db.SaveChangesAsync();
            }
            r=await Review(admin);var invalid=await Send(admin,ReviewPath+"/assignments",new ManualAssignmentRequest(r.Token,"ASSIGN",null,ids["employee"],d,new(2026,10,1)),"POST");
            Assert.Equal("NIGHT_WEEKLY_LIMIT",(await invalid.Content.ReadFromJsonAsync<ApiError>())!.Code);
            using var final=factory.Services.CreateScope();var p=await final.ServiceProvider.GetRequiredService<SchedulingInputs>().Read(2026,10,review:true);Assert.Equal(2,p.Work.Length);
        }
    }
    [PostgresFact]
    public async Task Constraint_pair_editor_is_atomic_and_ordinary_editor_cannot_break_pair()
    {
        var (admin,d,r)=await ManualSetup();using(admin)
        {
            await Semantic(d,SchedulingRules.OnCall);r=await Review(admin);
            var response=await Pair(admin,r,"ASSIGN",new(2026,10,2),ids["employee"]);Assert.Equal(HttpStatusCode.OK,response.StatusCode);r=(await response.Content.ReadFromJsonAsync<ReviewResponse>())!;
            Assert.Equal(2,r.Draft.Cells.Sum(c=>c.Employees.Length));Assert.Empty(r.Conflicts);Assert.Equal(0,r.Draft.Cells.Single(c=>c.Date==new DateOnly(2026,10,1)&&c.DefinitionId==d).RequiredEmployees);
            var a=r.Draft.Cells.SelectMany(c=>c.Employees).First();var remove=await Send(admin,ReviewPath+"/assignments",new ManualAssignmentRequest(r.Token,"REMOVE",a.AssignmentId,null,null,null),"POST");Assert.Equal("ON_CALL_PAIR_OPERATION_REQUIRED",(await remove.Content.ReadFromJsonAsync<ApiError>())!.Code);
            response=await Pair(admin,r,"REMOVE",new(2026,10,2),null,a.AssignmentId);Assert.Equal(HttpStatusCode.OK,response.StatusCode);r=(await response.Content.ReadFromJsonAsync<ReviewResponse>())!;Assert.Empty(r.Draft.Cells.SelectMany(c=>c.Employees));
        }
    }
    [PostgresFact]
    public async Task Constraint_pair_block_rolls_back_Friday_and_pair_can_be_counted_once_twice_not_three_times()
    {
        var (admin,d,r)=await ManualSetup();using(admin)
        {
            await Semantic(d,SchedulingRules.OnCall);await ConfigureWindow(admin,open:true);using var employee=await Login("employee");await Send(employee,"/api/availability/2026/10",Choices(0,3),"POST");r=await Review(admin);
            var blocked=await Pair(admin,r,"ASSIGN",new(2026,10,2),ids["employee"]);Assert.Equal("HARD_BLOCK_CONFLICT",(await blocked.Content.ReadFromJsonAsync<ApiError>())!.Code);Assert.Empty((await Review(admin)).Draft.Cells.SelectMany(c=>c.Employees));
            foreach(var day in new[]{9,16}){var response=await Pair(admin,await Review(admin),"ASSIGN",new(2026,10,day),ids["employee"]);Assert.Equal(HttpStatusCode.OK,response.StatusCode);}
            var third=await Pair(admin,await Review(admin),"ASSIGN",new(2026,10,23),ids["employee"]);Assert.Equal("WEEKEND_ALLOCATION_LIMIT",(await third.Content.ReadFromJsonAsync<ApiError>())!.Code);
            Assert.Equal(4,(await Review(admin)).Draft.Cells.Sum(c=>c.Employees.Length));
        }
    }
    [PostgresFact]
    public async Task Constraint_cross_month_pair_requires_explicit_confirmation_and_preserves_both_months()
    {
        var (admin,d,r)=await ManualSetup();using(admin)
        {
            await Semantic(d,SchedulingRules.OnCall);var path="/api/admin/scheduling/periods/2027/4";var review=(await admin.GetFromJsonAsync<ReviewResponse>(path))!;
            var input=new OnCallPairRequest(review.Token,"ASSIGN",new(2027,4,30),ids["employee"],null,false);
            var blocked=await Send(admin,path+"/on-call-pair",input,"POST");Assert.Equal("ON_CALL_ADJACENT_CONFIRMATION",(await blocked.Content.ReadFromJsonAsync<ApiError>())!.Code);
            Assert.Equal(HttpStatusCode.OK,(await Send(admin,path+"/on-call-pair",input with{AcknowledgeAdjacentPeriod=true},"POST")).StatusCode);
            var may=(await admin.GetFromJsonAsync<ReviewResponse>("/api/admin/scheduling/periods/2027/5"))!;Assert.Single(may.Draft.Cells.SelectMany(c=>c.Employees));Assert.Empty(may.Conflicts);
            using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();Assert.Equal(2,await db.Set<ShiftAssignment>().CountAsync());Assert.Equal(2,await db.Set<ScheduleEditAudit>().CountAsync());
        }
    }
    [PostgresFact]
    public async Task Constraint_existing_violations_block_both_preliminary_and_final_publication()
    {
        var (admin,d,r)=await ManualSetup();using(admin)
        {
            r=await Edit(admin,r,"ASSIGN",ids["employee"],d,1);r=await Ack(admin,r);await Publish(admin,r,"PRELIMINARY");
            // Historical persisted violations must be visible even if they bypassed the current editor.
            using(var scope=factory.Services.CreateScope())
            {
                var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var period=await db.Set<SchedulePeriod>().SingleAsync();
                foreach(var day in new[]{2,9,16}){var date=new DateOnly(2026,10,day);var t=ShiftClock.Boundaries(date,ShiftType.DAY);db.Add(new ShiftAssignment{UserId=ids["employee"],Shift=new Shift{SchedulePeriodId=period.Id,ShiftDate=date,StartsAt=t.StartsAt,EndsAt=t.EndsAt}});}await db.SaveChangesAsync();
            }
            r=await Review(admin);Assert.Contains(r.Conflicts,c=>c.Code=="WEEKEND_ALLOCATION_LIMIT");var final=await Send(admin,ReviewPath+"/publications/FINAL",new PublishScheduleRequest(r.Token,true,true,null),"POST");Assert.Equal(HttpStatusCode.Conflict,final.StatusCode);
            var correction=await Send(admin,ReviewPath+"/correction",new ReviewTokenRequest(r.Token),"POST");r=(await correction.Content.ReadFromJsonAsync<ReviewResponse>())!;
            var preliminary=await Send(admin,ReviewPath+"/publications/PRELIMINARY",new PublishScheduleRequest(r.Token,true,true,null),"POST");Assert.Equal(HttpStatusCode.Conflict,preliminary.StatusCode);
        }
    }
    [PostgresFact]
    public async Task Constraint_solver_respects_existing_monthly_allocations_and_saves_valid_draft()
    {
        var (admin,d,r)=await ManualSetup();using(admin)
        {
            r=await Edit(admin,r,"ASSIGN",ids["employee"],d,2);r=await Edit(admin,r,"ASSIGN",ids["employee"],d,9);
            var run=await Generate(admin);Assert.Equal("READY",run.State);Assert.Equal(2,run.Cells.Where(c=>SchedulingRules.Weekend(c.Date)).Sum(c=>c.Employees.Length));
            Assert.Equal(HttpStatusCode.OK,(await Send(admin,$"{GenerationPath}/{run.Id}/save",new{},"POST")).StatusCode);
            Assert.Empty((await Review(admin)).Conflicts);
        }
    }
    [PostgresFact]
    public async Task Constraint_neighboring_pair_edits_have_one_winner_without_partial_assignments()
    {
        var (admin,d,r)=await ManualSetup();using(admin)
        {
            await Semantic(d,SchedulingRules.OnCall);using var second=await Login();
            var april="/api/admin/scheduling/periods/2027/4";var may="/api/admin/scheduling/periods/2027/5";
            var a=(await admin.GetFromJsonAsync<ReviewResponse>(april))!;var b=(await second.GetFromJsonAsync<ReviewResponse>(may))!;
            var responses=await Task.WhenAll(Send(admin,april+"/on-call-pair",new OnCallPairRequest(a.Token,"ASSIGN",new(2027,4,30),ids["employee"],null,true),"POST"),Send(second,may+"/on-call-pair",new OnCallPairRequest(b.Token,"ASSIGN",new(2027,4,30),ids["employee"],null,true),"POST"));
            Assert.Single(responses,x=>x.StatusCode==HttpStatusCode.OK);Assert.Single(responses,x=>x.StatusCode==HttpStatusCode.Conflict);
            using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();Assert.Equal(2,await db.Set<ShiftAssignment>().CountAsync());Assert.Equal(2,await db.Set<ScheduleEditAudit>().CountAsync());
        }
    }
    [PostgresFact]
    public async Task Constraint_pair_edit_never_reopens_a_finalized_neighbor()
    {
        var (admin,d,r)=await ManualSetup();using(admin)
        {
            await Semantic(d,SchedulingRules.OnCall);var april="/api/admin/scheduling/periods/2027/4";var may="/api/admin/scheduling/periods/2027/5";
            var a=(await admin.GetFromJsonAsync<ReviewResponse>(april))!;
            Assert.Equal(HttpStatusCode.OK,(await Send(admin,april+"/on-call-pair",new OnCallPairRequest(a.Token,"ASSIGN",new(2027,4,30),ids["employee"],null,true),"POST")).StatusCode);
            var m=(await admin.GetFromJsonAsync<ReviewResponse>(may))!;
            var ack=await Send(admin,may+"/review",new ReviewTokenRequest(m.Token),"POST");Assert.Equal(HttpStatusCode.OK,ack.StatusCode);m=(await ack.Content.ReadFromJsonAsync<ReviewResponse>())!;
            await Send(admin,may+"/publications/PRELIMINARY",new PublishScheduleRequest(m.Token,true,true,null),"POST");m=(await admin.GetFromJsonAsync<ReviewResponse>(may))!;
            Assert.Equal(HttpStatusCode.OK,(await Send(admin,may+"/publications/FINAL",new PublishScheduleRequest(m.Token,true,true,null),"POST")).StatusCode);
            a=(await admin.GetFromJsonAsync<ReviewResponse>(april))!;var source=a.Draft.Cells.SelectMany(c=>c.Employees).Single();
            var remove=await Send(admin,april+"/on-call-pair",new OnCallPairRequest(a.Token,"REMOVE",new(2027,4,30),null,source.AssignmentId,true),"POST");Assert.Equal("INVALID_SCHEDULE_TRANSITION",(await remove.Content.ReadFromJsonAsync<ApiError>())!.Code);
            using var scope=factory.Services.CreateScope();Assert.Equal(2,await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Set<ShiftAssignment>().CountAsync());
        }
    }
    [PostgresFact]
    public async Task Constraint_legacy_cleanup_can_remove_violations_one_at_a_time()
    {
        var (admin,d,r)=await ManualSetup();using(admin)
        {
            await Semantic(d,SchedulingRules.OnCall);
            foreach(var day in new[]{2,9})Assert.Equal(HttpStatusCode.OK,(await Pair(admin,await Review(admin),"ASSIGN",new(2026,10,day),ids["employee"])).StatusCode);
            using(var scope=factory.Services.CreateScope())
            {
                var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var period=await db.Set<SchedulePeriod>().SingleAsync();
                foreach(var day in new[]{16,23}){var date=new DateOnly(2026,10,day);var t=ShiftClock.Boundaries(date,ShiftType.DAY);db.Add(new ShiftAssignment{UserId=ids["employee"],Shift=new Shift{SchedulePeriodId=period.Id,ShiftDate=date,StartsAt=t.StartsAt,EndsAt=t.EndsAt}});}await db.SaveChangesAsync();
            }
            r=await Review(admin);var source=r.Draft.Cells.Single(c=>c.Date==new DateOnly(2026,10,2)&&c.DefinitionId==d).Employees.Single();
            Assert.Equal(HttpStatusCode.OK,(await Pair(admin,r,"REMOVE",new(2026,10,2),null,source.AssignmentId)).StatusCode);
            Assert.Contains((await Review(admin)).Conflicts,c=>c.Code=="WEEKEND_ALLOCATION_LIMIT");
        }
    }
}
