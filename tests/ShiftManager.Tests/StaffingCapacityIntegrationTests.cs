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
    public async Task Generation_and_preview_save_reject_retained_zero_demand_in_excluded_row_without_deleting_work()
    {
        using var admin=await Login();await ConfigureAutomatic(1);
        var excluded=BoardDefinition(6);Guid assignment;
        using(var scope=factory.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var period=new SchedulePeriod{Year=2026,Month=10};var date=new DateOnly(2026,10,1);var time=ShiftClock.Boundaries(date,ShiftType.DAY);
            var run=new SchedulingRun{Id=Guid.NewGuid(),Year=2026,Month=10,SavedByUserId=ids["admin"],SavedAt=DateTime.UtcNow};
            var cell=new SchedulingCell{ShiftTypeDefinitionId=excluded,SchedulePeriod=period,Date=date,StartsAt=time.StartsAt,EndsAt=time.EndsAt,RequiredEmployees=1,SchedulingRunId=run.Id};
            var a=new ShiftAssignment{UserId=ids["employee"],IsLocked=true,Cell=cell,Shift=new Shift{SchedulePeriod=period,ShiftDate=date,StartsAt=time.StartsAt,EndsAt=time.EndsAt}};
            db.Add(run);db.Add(a);await db.SaveChangesAsync();assignment=a.Id;
        }
        using(var scope=factory.Services.CreateScope())
        {
            var p=await scope.ServiceProvider.GetRequiredService<SchedulingInputs>().Read(2026,10);
            var forged=p.Cells.Select(c=>new GeneratedCell(c.DefinitionId,c.Date,c.Start,c.End,c.Required,[])).ToArray();
            var error=Assert.Throws<ApiException>(()=>GeneratedDraftService.Validate(p,forged));
            Assert.Equal("STAFFING_EXCEEDED",error.Code);
        }
        var generated=await Generate(admin);
        Assert.Equal("FAILED",generated.State);Assert.Equal("STAFFING_EXCEEDED",generated.ErrorCode);
        var board=(await admin.GetFromJsonAsync<WeeklyBoardResponse>("/api/admin/scheduling/board?date=2026-10-01"))!;
        var retained=board.Rows.Single(r=>r.Definition.Id==excluded).Cells.Single(c=>c.Date==new DateOnly(2026,10,1));
        Assert.Equal(0,retained.RequiredEmployees);Assert.Single(retained.Employees!);
        var review=await Review(admin);Assert.Contains(review.Conflicts,c=>c.Code=="STAFFING_EXCEEDED");
        var publication=await Send(admin,ReviewPath+"/publications/PRELIMINARY",new PublishScheduleRequest(review.Token,true,true,null),"POST");
        Assert.Equal(HttpStatusCode.Conflict,publication.StatusCode);
        using var final=factory.Services.CreateScope();var context=final.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(await context.Set<ShiftAssignment>().AnyAsync(a=>a.Id==assignment));
    }
}
