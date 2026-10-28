using ShiftManager.Api;
using ShiftManager.Domain;
using ShiftManager.Services.AutomaticScheduling;
namespace ShiftManager.Tests;
public sealed class AdditionalSchedulingRulesTests
{
    private static readonly Guid User = Guid.NewGuid();
    private static readonly Guid Row = Guid.NewGuid();
    private static CandidateCell Cell(DateOnly date, string semantic = "ORDINARY", ShiftType type = ShiftType.DAY, bool expert = false)
    {
        var t = ShiftClock.Boundaries(date, type); return new(Row,date,t.StartsAt,t.EndsAt,1,1,type,expert,semantic);
    }
    private static Work Work(DateOnly date, string semantic = "ORDINARY", ShiftType type = ShiftType.DAY) => SchedulingRules.Proposed(User,Cell(date,semantic,type));
    private static SchedulingProblem Problem(CandidateCell[] cells, Work[]? work = null, Preference[]? prefs = null) =>
        new(cells[0].Date.Year,cells[0].Date.Month,"fingerprint",cells,[new(User,"עובד",true)],work??[],prefs??[]);
    private static OptimizationResult Solve(SchedulingProblem p) => new ScheduleOptimizer().Solve(p,5,CancellationToken.None);
    [Theory]
    [InlineData(0,false)] [InlineData(1,false)] [InlineData(2,false)] [InlineData(3,true)]
    public void Monthly_weekend_limit_counts_zero_one_two_and_rejects_three(int count,bool invalid)
    {
        var days=new[]{new DateOnly(2026,10,2),new DateOnly(2026,10,9),new DateOnly(2026,10,16)};
        Assert.Equal(invalid,SchedulingRules.Violations(days.Take(count).Select(d=>Work(d)),2026,10).Any(v=>v.Code=="WEEKEND_ALLOCATION_LIMIT"));
    }
    [Theory] [InlineData(1,false)] [InlineData(2,false)] [InlineData(3,true)]
    public void On_call_pairs_count_one_allocation_each(int pairs,bool invalid)
    {
        var work=Enumerable.Range(0,pairs).SelectMany(i=>new[]{Work(new DateOnly(2026,10,2).AddDays(i*7),SchedulingRules.OnCall),Work(new DateOnly(2026,10,3).AddDays(i*7),SchedulingRules.OnCall)}).ToArray();
        Assert.Equal(pairs,SchedulingRules.Allocations(work));Assert.Equal(invalid,SchedulingRules.Violations(work,2026,10).Any());
    }
    [Fact] public void A_pair_and_an_ordinary_weekend_are_two_but_two_ordinary_weekends_are_three()
    {
        var work=new[]{Work(new(2026,10,2),SchedulingRules.OnCall),Work(new(2026,10,3),SchedulingRules.OnCall),Work(new(2026,10,9))};
        Assert.Empty(SchedulingRules.Violations(work,2026,10));Assert.Contains(SchedulingRules.Violations(work.Append(Work(new(2026,10,16))),2026,10),v=>v.Code=="WEEKEND_ALLOCATION_LIMIT");
    }
    [Fact] public void Cross_month_pair_is_charged_to_Friday_month()
    {
        var pair=new[]{Work(new(2027,4,30),SchedulingRules.OnCall),Work(new(2027,5,1),SchedulingRules.OnCall)};
        Assert.All(pair,w=>Assert.Equal(new DateOnly(2027,4,1),SchedulingRules.AllocationMonth(w)));
        Assert.Empty(SchedulingRules.Violations(pair.Append(Work(new(2027,5,7))).Append(Work(new(2027,5,14))),2027,5));
        Assert.Contains(SchedulingRules.Violations(pair.Append(Work(new(2027,4,2))).Append(Work(new(2027,4,9))),2027,4),v=>v.Code=="WEEKEND_ALLOCATION_LIMIT");
    }
    [Fact] public void Weekly_semantic_limit_crosses_month_and_does_not_apply_to_similar_time_ranges()
    {
        var work=new[]{Work(new(2026,9,30),SchedulingRules.SixNine),Work(new(2026,10,1),SchedulingRules.SixNine)};
        Assert.Contains(SchedulingRules.Violations(work,2026,10),v=>v.Code=="SIX_NINE_WEEKLY_LIMIT");
        Assert.Empty(SchedulingRules.Violations(work.Select(w=>w with{Semantic="ORDINARY"}),2026,10));
    }
    [Fact] public void Weekly_nights_count_all_rows_and_adjacent_months()
    {
        var work=new[]{Work(new(2026,9,29),type:ShiftType.NIGHT),Work(new(2026,9,30),type:ShiftType.NIGHT),Work(new(2026,10,1),type:ShiftType.NIGHT)};
        Assert.Contains(SchedulingRules.Violations(work,2026,10),v=>v.Code=="NIGHT_WEEKLY_LIMIT");
    }
    [Fact] public void Unmatched_Friday_Saturday_and_weekday_on_call_are_rejected()
    {
        foreach(var date in new[]{new DateOnly(2026,10,2),new DateOnly(2026,10,3)})Assert.Contains(SchedulingRules.Violations([Work(date,SchedulingRules.OnCall)],2026,10),v=>v.Code=="ON_CALL_PAIR_REQUIRED");
        Assert.Contains(SchedulingRules.Violations([Work(new(2026,10,1),SchedulingRules.OnCall)],2026,10),v=>v.Code=="ON_CALL_WEEKEND_ONLY");
    }
    [Fact] public void Solver_jointly_limits_weekends_and_semantic_weekly_shifts()
    {
        var dates=Enumerable.Range(1,31).Select(d=>new DateOnly(2026,10,d)).ToArray();var p=Problem(dates.Select(d=>Cell(d,SchedulingRules.SixNine)).ToArray());
        var r=Solve(p);Assert.All(r.Cells.SelectMany(c=>c.Employees),e=>Assert.Equal(User,e.UserId));
        Assert.All(r.Cells.Where(c=>c.Employees.Length>0).GroupBy(c=>SchedulingRules.Week(c.Date)),g=>Assert.Single(g));
        Assert.True(r.Cells.Count(c=>c.Employees.Length>0&&SchedulingRules.Weekend(c.Date))<=2);GeneratedDraftService.Validate(p,r.Cells);
    }
    [Fact] public void Solver_pairs_atomically_and_a_block_on_Saturday_leaves_both_short()
    {
        var friday=Cell(new(2026,10,2),SchedulingRules.OnCall);var saturday=Cell(new(2026,10,3),SchedulingRules.OnCall);var p=Problem([friday,saturday]);
        var r=Solve(p);Assert.All(r.Cells,c=>Assert.Single(c.Employees));GeneratedDraftService.Validate(p,r.Cells);
        p=p with{Preferences=[new(User,saturday.Date,ShiftType.DAY,PreferenceType.BLOCKED)]};r=Solve(p);Assert.All(r.Cells,c=>Assert.Empty(c.Employees));GeneratedDraftService.Validate(p,r.Cells);
    }
    [Fact] public void Solver_boundary_pair_requires_existing_counterpart_without_writing_it()
    {
        var friday=Cell(new(2027,4,30),SchedulingRules.OnCall);var p=Problem([friday]);Assert.Empty(Solve(p).Cells.Single().Employees);
        p=p with{Work=[Work(new(2027,5,1),SchedulingRules.OnCall)]};var result=Solve(p);Assert.Single(result.Cells.Single().Employees);GeneratedDraftService.Validate(p,result.Cells);
    }
    [Fact] public void Solver_existing_nights_consume_weekly_capacity()
    {
        var p=Problem([Cell(new(2026,10,1),type:ShiftType.NIGHT)], [Work(new(2026,9,27),type:ShiftType.NIGHT),Work(new(2026,9,29),type:ShiftType.NIGHT)]);
        Assert.Empty(Solve(p).Cells.Single().Employees);
    }
    [Fact] public void Save_validation_rejects_a_forged_third_weekend()
    {
        var p=Problem([Cell(new(2026,10,16))],[Work(new(2026,10,2)),Work(new(2026,10,9))]);var c=p.Cells[0];
        var exception=Assert.Throws<ApiException>(()=>GeneratedDraftService.Validate(p,[new(c.DefinitionId,c.Date,c.Start,c.End,1,[new(User,"עובד",false,false)])]));Assert.Equal("WEEKEND_ALLOCATION_LIMIT",exception.Code);
    }
    [Fact] public void Solver_limits_three_possible_pairs_to_two_complete_pairs()
    {
        var cells=new[]{2,3,9,10,16,17}.Select(d=>Cell(new(2026,10,d),SchedulingRules.OnCall)).ToArray();var p=Problem(cells);var r=Solve(p);
        Assert.Equal(4,r.Cells.Sum(c=>c.Employees.Length));GeneratedDraftService.Validate(p,r.Cells);
        foreach(var pair in r.Cells.GroupBy(c=>SchedulingRules.Friday(c.Date)))Assert.Equal(pair.First().Employees.Length,pair.Last().Employees.Length);
    }
    [Fact] public void Boundary_Saturday_reserves_Fridays_month_not_its_own_full_month()
    {
        var p=Problem([Cell(new(2027,5,1),SchedulingRules.OnCall)],[Work(new(2027,4,30),SchedulingRules.OnCall),Work(new(2027,4,2)),Work(new(2027,5,7)),Work(new(2027,5,14))]);
        var r=Solve(p);Assert.Single(r.Cells.Single().Employees);GeneratedDraftService.Validate(p,r.Cells);
    }
}
