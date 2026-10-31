using ShiftManager.Development;
using ShiftManager.Domain;
using ShiftManager.Services.AutomaticScheduling;
namespace ShiftManager.Tests;

public sealed class DemoConstraintScenariosTests
{
    private static readonly Guid User = DemoPlan.Accounts(42)[1].Id;
    private static readonly Guid Row = Guid.Parse("40000000-0000-0000-0000-000000000026");
    private static Work Work(DateOnly date, ShiftType type=ShiftType.DAY, string semantic="ORDINARY")
    {
        var times=ShiftClock.Boundaries(date,type);
        return new(Guid.NewGuid(),User,Row,date,times.StartsAt,times.EndsAt,Guid.Empty,false,false,type,semantic);
    }
    private static Work[] Pair(DateOnly friday) => [Work(friday,semantic:SchedulingRules.OnCall),Work(friday.AddDays(1),semantic:SchedulingRules.OnCall)];
    [Fact]
    public void Demo_deputy_may_have_two_on_call_pairs_including_a_cross_month_pair_but_not_three()
    {
        var work=Pair(new(2025,1,24)).Concat(Pair(new(2025,1,31))).ToArray();
        Assert.Empty(SchedulingRules.Violations(work,2025,1));
        Assert.Empty(SchedulingRules.Violations(work,2025,2));
        Assert.Equal(2,SchedulingRules.Allocations(work));
        Assert.Contains(SchedulingRules.Violations(work.Concat(Pair(new(2025,1,17))),2025,1),v=>v.Code=="WEEKEND_ALLOCATION_LIMIT");
        Assert.Contains(SchedulingRules.Violations(work.Append(Work(new(2025,1,10))),2025,1),v=>v.Code=="WEEKEND_ALLOCATION_LIMIT");
        Assert.Contains(SchedulingRules.Violations(work[..3],2025,1),v=>v.Code=="ON_CALL_PAIR_REQUIRED");
        Assert.Contains(SchedulingRules.Violations([Work(new(2025,1,20),semantic:SchedulingRules.OnCall)],2025,1),v=>v.Code=="ON_CALL_WEEKEND_ONLY");
    }
    [Fact]
    public void Demo_nights_and_six_nine_limits_include_previous_calendar_month_in_same_sunday_week()
    {
        var nights=new[]{Work(new(2026,3,30),ShiftType.NIGHT),Work(new(2026,3,31),ShiftType.NIGHT)};
        Assert.Empty(SchedulingRules.Violations(nights,2026,3));
        Assert.Contains(SchedulingRules.Violations(nights.Append(Work(new(2026,4,1),ShiftType.NIGHT)),2026,4),v=>v.Code=="NIGHT_WEEKLY_LIMIT");
        var sixNine=new[]{Work(new(2026,3,31),semantic:SchedulingRules.SixNine),Work(new(2026,4,2),semantic:SchedulingRules.SixNine)};
        Assert.Contains(SchedulingRules.Violations(sixNine,2026,4),v=>v.Code=="SIX_NINE_WEEKLY_LIMIT");
        Assert.Empty(SchedulingRules.Violations([sixNine[0],Work(new(2026,4,5),semantic:SchedulingRules.SixNine)],2026,4));
    }
}
