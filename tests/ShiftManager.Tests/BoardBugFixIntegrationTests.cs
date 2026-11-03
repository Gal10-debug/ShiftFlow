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
    [PostgresFact]
    public async Task Browser_JSON_binds_manual_commands_and_Hebrew_notes_at_exact_limits_and_reloads_without_mutating_history()
    {
        var (admin, definition, r) = await ManualSetup(); using (admin)
        {
            Assert.Equal(1,r.EditingContractVersion);
            async Task<ReviewResponse> Browser(string operation, Guid? assignment, string? note, int? day = null, Guid? user = null)
            {
                // Exact camelCase JSON shape sent by ScheduleReviewPanel, including nullable fields.
                var response = await Send(admin,ReviewPath+"/assignments",new {
                    expectedToken=r.Token,operation,assignmentId=assignment,userId=user,
                    definitionId=definition,date=day.HasValue ? $"2026-10-{day:00}" : null,note
                },"POST");
                Assert.Equal(HttpStatusCode.OK,response.StatusCode);
                return (await response.Content.ReadFromJsonAsync<ReviewResponse>())!;
            }
            r = await Browser("ASSIGN",null,null,1,ids["employee"]);
            var a = r.Draft.Cells.SelectMany(c=>c.Employees).Single();
            foreach(var note in new[]{"להגיע חצי שעה מוקדם\nהערה לעובד",new string('א',1000),"",null})
            {
                r=await Browser("NOTE",a.AssignmentId,note);
                var loaded=await Review(admin);
                Assert.Equal(string.IsNullOrEmpty(note)?null:note,loaded.Draft.Cells.SelectMany(c=>c.Employees).Single().Note);
                r=loaded;
            }
            var oversized=await Send(admin,ReviewPath+"/assignments",new {expectedToken=r.Token,operation="NOTE",assignmentId=a.AssignmentId,userId=(Guid?)null,definitionId=definition,date=(string?)null,note=new string('א',1001)},"POST");
            Assert.Equal(HttpStatusCode.BadRequest,oversized.StatusCode);
            Assert.Equal("INVALID_ASSIGNMENT_NOTE",(await oversized.Content.ReadFromJsonAsync<ApiError>())!.FieldErrors!["note"]);
            r=await Browser("NOTE",a.AssignmentId,"הערה מפורסמת");
            r=await Ack(admin,r);var v1=await Publish(admin,r,"PRELIMINARY");
            var locked=await Send(admin,ReviewPath+"/assignments",new {expectedToken=(await Review(admin)).Token,operation="NOTE",assignmentId=a.AssignmentId,userId=(Guid?)null,definitionId=definition,date=(string?)null,note="שינוי אסור"},"POST");
            Assert.Equal(HttpStatusCode.Conflict,locked.StatusCode);
            r=await Review(admin);var correction=await Send(admin,ReviewPath+"/correction",new ReviewTokenRequest(r.Token),"POST");
            r=(await correction.Content.ReadFromJsonAsync<ReviewResponse>())!;
            r=await Browser("NOTE",a.AssignmentId,"הערה בטיוטה");
            using var employee=await Login("employee");
            var frozen=(await employee.GetFromJsonAsync<PublicationResponse>("/api/my-schedule/2026/10"))!;
            Assert.Equal(v1.Id,frozen.Id);Assert.Equal("הערה מפורסמת",frozen.Snapshot!.Cells.SelectMany(c=>c.Employees).Single().Note);
            Assert.Equal(HttpStatusCode.Forbidden,(await Send(employee,ReviewPath+"/assignments",new {expectedToken=r.Token,operation="NOTE",assignmentId=a.AssignmentId,note="אסור"},"POST")).StatusCode);
            r=await Browser("MOVE",a.AssignmentId,null,4);
            a=r.Draft.Cells.SelectMany(c=>c.Employees).Single();Assert.Equal("הערה בטיוטה",a.Note);
            r=await Browser("REPLACE",a.AssignmentId,null,null,ids["employee"]);
            a=r.Draft.Cells.SelectMany(c=>c.Employees).Single();Assert.Null(a.Note);
            r=await Browser("REMOVE",a.AssignmentId,null);
            Assert.Empty((await Review(admin)).Draft.Cells.SelectMany(c=>c.Employees));
        }
    }

    [PostgresFact]
    public async Task Row3_weekend_capacity_is_one_for_solver_board_manual_add_replace_move_and_publication()
    {
        using var admin=await Login();
        var definition=BoardDefinition(3);
        using(var scope=factory.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var d=await db.Set<ShiftTypeDefinition>().SingleAsync(d=>d.Id==definition);
            d.RequiredEmployees=3;d.IncludeInAutomaticScheduling=true;await db.SaveChangesAsync();
        }
        await SetupTeam(admin);
        var r=await Review(admin);
        r=await Edit(admin,r,"ASSIGN",ids["employee"],definition,2);
        var cell=r.Draft.Cells.Single(c=>c.DefinitionId==definition&&c.Date==new DateOnly(2026,10,2));
        Assert.Equal(1,cell.RequiredEmployees);Assert.Single(cell.Employees);
        Assert.Equal(3,r.Draft.Cells.Single(c=>c.DefinitionId==definition&&c.Date==new DateOnly(2026,10,1)).RequiredEmployees);
        var a=cell.Employees.Single();
        var rejected=await Send(admin,ReviewPath+"/assignments",new ManualAssignmentRequest(r.Token,"ASSIGN",null,ids["coordinator"],definition,new(2026,10,2)),"POST");
        Assert.Equal("STAFFING_EXCEEDED",(await rejected.Content.ReadFromJsonAsync<ApiError>())!.Code);
        using(var scope=factory.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.Set<SchedulingCell>().SingleAsync(c=>c.ShiftTypeDefinitionId==definition)).RequiredEmployees=3;
            await db.SaveChangesAsync();
        }
        r=await Review(admin);
        r=await Edit(admin,r,"REPLACE",ids["employee"],null,2,a.AssignmentId);
        a=r.Draft.Cells.Single(c=>c.DefinitionId==definition&&c.Date==new DateOnly(2026,10,2)).Employees.Single();
        r=await Edit(admin,r,"MOVE",null,definition,3,a.AssignmentId);
        var board=(await admin.GetFromJsonAsync<WeeklyBoardResponse>("/api/admin/scheduling/board?date=2026-10-03"))!;
        var saturday=board.Rows.Single(row=>row.Definition.Id==definition).Cells.Single(c=>c.Date==new DateOnly(2026,10,3));
        Assert.Equal(1,saturday.RequiredEmployees);Assert.Equal(0,saturday.Shortage);
        using(var scope=factory.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.All(await db.Set<SchedulingCell>().Where(c=>c.ShiftTypeDefinitionId==definition).ToArrayAsync(),c=>Assert.Equal(1,c.RequiredEmployees));
        }
        var generated=await Generate(admin);Assert.Equal("READY",generated.State);
        Assert.All(generated.Cells.Where(c=>c.DefinitionId==definition&&SchedulingRules.Weekend(c.Date)),c=>{Assert.Equal(1,c.RequiredEmployees);Assert.True(c.Employees.Length<=1);});
        r=await Ack(admin,await Review(admin));var published=await Publish(admin,r,"PRELIMINARY");
        Assert.Equal(1,published.Snapshot!.Cells.Single(c=>c.DefinitionId==definition&&c.Date==new DateOnly(2026,10,3)).RequiredEmployees);
    }
    [PostgresFact]
    public async Task Row3_zero_weekday_staffing_still_generates_only_its_weekend_demand_when_enabled()
    {
        using var admin=await Login();var id=BoardDefinition(3);
        using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var d=await db.Set<ShiftTypeDefinition>().SingleAsync(d=>d.Id==id);
        d.RequiredEmployees=0;d.IncludeInAutomaticScheduling=true;await db.SaveChangesAsync();
        var p=await scope.ServiceProvider.GetRequiredService<SchedulingInputs>().Read(2026,10);
        Assert.Equal(31,p.Cells.Length);
        Assert.All(p.Cells,c=>Assert.Equal(SchedulingRules.Weekend(c.Date)?1:0,c.Required));
        var definition=(await Types(admin)).Single(d=>d.Id==id);Assert.Equal(0,definition.RequiredEmployees);Assert.Equal(1,definition.WeekendRequiredEmployees);
        var result=new ScheduleOptimizer().Solve(p,5,CancellationToken.None);
        GeneratedDraftService.Validate(p,result.Cells);
        Assert.All(result.Cells.Where(c=>!SchedulingRules.Weekend(c.Date)),c=>Assert.Empty(c.Employees));
    }

}
