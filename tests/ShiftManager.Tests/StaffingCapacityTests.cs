using ShiftManager.Api;
using ShiftManager.Domain;
using ShiftManager.Services.AutomaticScheduling;
namespace ShiftManager.Tests;
public sealed class StaffingCapacityTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(7)]
    public void Displayed_cell_requirement_is_solver_target_and_absolute_capacity(int required)
    {
        var date=new DateOnly(2026,11,2);var t=ShiftClock.Boundaries(date,ShiftType.DAY);
        var c=new CandidateCell(Guid.NewGuid(),date,t.StartsAt,t.EndsAt,required,1,ShiftType.DAY);
        var employees=Enumerable.Range(0,9).Select(i=>new Employee(Guid.NewGuid(),$"עובד {i}")).ToArray();
        var p=new SchedulingProblem(2026,11,"test",[c],employees,[],[]);
        var result=new ScheduleOptimizer().Solve(p,5,CancellationToken.None);
        var output=Assert.Single(result.Cells);
        Assert.Equal(required,output.RequiredEmployees);Assert.Equal(required,output.Employees.Length);Assert.Equal(0,output.Shortage);
        GeneratedDraftService.Validate(p,result.Cells);
        if(required==0)Assert.All(employees,e=>Assert.False(p.Eligible(e,c)));
        var insufficient=p with{Employees=employees.Take(1).ToArray()};
        var partial=new ScheduleOptimizer().Solve(insufficient,5,CancellationToken.None).Cells.Single();
        Assert.Equal(Math.Min(1,required),partial.Employees.Length);Assert.Equal(Math.Max(0,required-1),partial.Shortage);
    }
    [Fact]
    public void Both_solver_and_preview_validation_reject_excluded_retained_overstaffing()
    {
        var date=new DateOnly(2026,11,2);var t=ShiftClock.Boundaries(date,ShiftType.DAY);
        var c=new CandidateCell(Guid.NewGuid(),date,t.StartsAt,t.EndsAt,0,1,ShiftType.DAY);
        var e=new Employee(Guid.NewGuid(),"עובד");var work=SchedulingRules.Proposed(e.Id,c);
        var p=new SchedulingProblem(2026,11,"test",[],[e],[work],[],[c]);
        Assert.True(p.ExceedsCapacity(p.Work));
        Assert.Equal("STAFFING_EXCEEDED",Assert.Throws<ApiException>(()=>new ScheduleOptimizer().Solve(p,5,CancellationToken.None)).Code);
        Assert.Equal("STAFFING_EXCEEDED",Assert.Throws<ApiException>(()=>GeneratedDraftService.Validate(p,[])).Code);
        var positive=p with{StaffingCells=[c with{Required=1}]};
        Assert.False(positive.ExceedsCapacity(positive.Work));
        Assert.True(positive.ExceedsCapacity(positive.Work.Append(work with{AssignmentId=Guid.NewGuid(),UserId=Guid.NewGuid()})));
    }
}
