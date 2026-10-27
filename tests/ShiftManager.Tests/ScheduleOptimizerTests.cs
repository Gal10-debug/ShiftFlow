using ShiftManager.Api;
using ShiftManager.Domain;
using ShiftManager.Services.AutomaticScheduling;
namespace ShiftManager.Tests;

public sealed class ScheduleOptimizerTests
{
    private static readonly DateOnly Date = new(2026, 10, 1);
    private static Employee Employee(int i) => new(Guid.Parse($"00000000-0000-0000-0001-{i:000000000000}"), $"עובד {i}");
    private static CandidateCell Cell(int i, int day = 0, ShiftTimeRange range = ShiftTimeRange.DAY, int required = 1)
    {
        var date = Date.AddDays(day); var times = ShiftTimeRanges.Boundaries(date, range);
        return new(Guid.Parse($"00000000-0000-0000-0002-{i:000000000000}"), date, times.StartsAt, times.EndsAt, required, 1, ShiftTimeRanges.Classification(range));
    }
    private static SchedulingProblem Problem(CandidateCell[] cells, int employees = 3, Work[]? work = null, Preference[]? preferences = null) =>
        new(2026, 10, "fingerprint", cells, Enumerable.Range(1, employees).Select(Employee).ToArray(), work ?? [], preferences ?? []);
    private static OptimizationResult Solve(SchedulingProblem p) => new ScheduleOptimizer().Solve(p, 10, CancellationToken.None);
    [Fact]
    public void Organizational_leader_is_never_eligible_even_when_expert_qualified()
    {
        var leader = Employee(1) with { IsShiftLeadQualified = true, IsTeamLeader = true };
        var deputy = Employee(2);
        var cells = new[] { Cell(1), Cell(2, 1) with { RequiresShiftLeadQualification = true } };
        var p = Problem(cells) with { Employees = [leader, deputy] };
        Assert.All(cells, c => Assert.False(p.Eligible(leader, c)));
        var r = Solve(p);
        Assert.Equal(deputy.Id, r.Cells[0].Employees.Single().UserId);
        Assert.Empty(r.Cells[1].Employees);
        GeneratedDraftService.Validate(p, r.Cells);
    }
    [Fact]
    public void Deputy_remains_bound_by_blocks_overlaps_and_no_consecutive_shifts()
    {
        var deputy = Employee(1);
        var cells = new[] { Cell(1), Cell(2, range: ShiftTimeRange.NIGHT), Cell(3, 1), Cell(4, 1, ShiftTimeRange.H07_13) };
        var p = Problem(cells, 1, preferences: [new(deputy.Id, Date, ShiftType.DAY, PreferenceType.BLOCKED)]);
        var r = Solve(p);
        Assert.Empty(r.Cells[0].Employees);
        Assert.Equal(1, r.Cells.Sum(c => c.Employees.Length));
        GeneratedDraftService.Validate(p, r.Cells);
    }
    [Fact]
    public void Variable_requirements_partial_staffing_and_overlaps()
    {
        var p = Problem([Cell(1, required: 2), Cell(2, required: 3), Cell(3, required: 0)]);
        var r = Solve(p);
        Assert.Equal("OPTIMAL", r.Status);
        Assert.Equal(3, r.Cells.Sum(c => c.Employees.Length));
        Assert.Equal(2, StaffingSummary.From(r.Cells).Missing);
        Assert.Empty(r.Cells[2].Employees);
        Assert.Equal(3, r.Cells.SelectMany(c => c.Employees).DistinctBy(e => e.UserId).Count());
        GeneratedDraftService.Validate(p, r.Cells);
    }
    [Fact]
    public void Day_night_next_day_and_cross_month_conflicts_are_hard()
    {
        var before = Cell(99, -1, ShiftTimeRange.NIGHT);
        var work = new Work(Guid.NewGuid(), Employee(1).Id, null, before.Date, before.Start, before.End, Guid.NewGuid(), false, false, ShiftType.NIGHT);
        var p = Problem([Cell(1), Cell(2, range: ShiftTimeRange.NIGHT), Cell(3, 1)], 1, [work]);
        var r = Solve(p);
        Assert.Empty(r.Cells[0].Employees);
        Assert.Equal(1, r.Cells.Sum(c => c.Employees.Length));
        GeneratedDraftService.Validate(p, r.Cells);
    }
    [Fact]
    public void Following_month_commitment_prevents_last_night()
    {
        var after = Cell(99, 31);
        var work = new Work(Guid.NewGuid(), Employee(1).Id, null, after.Date, after.Start, after.End, Guid.NewGuid(), false, false, ShiftType.DAY);
        var r = Solve(Problem([Cell(1, 30, ShiftTimeRange.NIGHT)], 1, [work]));
        Assert.Empty(r.Cells.Single().Employees);
    }
    [Fact]
    public void Custom_range_checks_all_overlapping_block_anchors()
    {
        var p = Problem([Cell(1, range: ShiftTimeRange.H06_09), Cell(2, 1, ShiftTimeRange.H15_23)], 1,
            preferences: [new(Employee(1).Id, Date.AddDays(-1), ShiftType.NIGHT, PreferenceType.BLOCKED), new(Employee(1).Id, Date.AddDays(1), ShiftType.NIGHT, PreferenceType.BLOCKED)]);
        Assert.All(Solve(p).Cells, c => Assert.Empty(c.Employees));
    }
    [Fact]
    public void Fairness_precedes_preferences_and_preferences_break_fair_ties()
    {
        var cells = Enumerable.Range(0, 6).Select(i => Cell(i + 1, i)).ToArray();
        var p = Problem(cells, 2, preferences: cells.Select(c => new Preference(Employee(1).Id, c.Date, ShiftType.DAY, PreferenceType.PREFERRED)).ToArray());
        var r = Solve(p);
        Assert.Equal(3, r.Cells.SelectMany(c => c.Employees).Count(e => e.UserId == Employee(1).Id));
        var one = Solve(Problem([Cell(1)], 2, preferences: [new(Employee(2).Id, Date, ShiftType.DAY, PreferenceType.PREFERRED)]));
        Assert.Equal(Employee(2).Id, one.Cells.Single().Employees.Single().UserId);
    }
    [Fact]
    public void Protected_work_counts_toward_capacity_and_fairness()
    {
        var c = Cell(1);
        var fixedWork = new Work(Guid.NewGuid(), Employee(1).Id, c.DefinitionId, c.Date, c.Start, c.End, Guid.NewGuid(), false, true, ShiftType.DAY);
        var p = Problem([c, Cell(2, 1)], 2, [fixedWork]);
        var r = Solve(p);
        Assert.True(r.Cells[0].Employees.Single().Protected);
        Assert.True(r.Cells[0].Employees.Single().IsShiftLead);
        Assert.Equal(Employee(2).Id, r.Cells[1].Employees.Single().UserId);
        GeneratedDraftService.Validate(p, r.Cells);
    }
    [Fact]
    public void Impossible_protected_capacity_fails_without_removing_members()
    {
        var c = Cell(1, required: 0);
        var w = new Work(Guid.NewGuid(), Employee(1).Id, c.DefinitionId, c.Date, c.Start, c.End, Guid.NewGuid(), false, false, ShiftType.DAY);
        Assert.Throws<ApiException>(() => Solve(Problem([c], 1, [w])));
    }
    [Fact]
    public void Coverage_is_not_greedy_and_prefers_two_legal_intervals_over_one_long_interval()
    {
        var p = Problem([Cell(1, range: ShiftTimeRange.NIGHT), Cell(2), Cell(3, 1)], 1);
        var r = Solve(p);
        Assert.Equal(2, r.Cells.Sum(c => c.Employees.Length));
        GeneratedDraftService.Validate(p, r.Cells);
    }
    [Fact]
    public void Regeneration_preserves_previous_choice_after_other_objectives()
    {
        var c = Cell(1); var employee = Employee(2);
        var w = new Work(Guid.NewGuid(), employee.Id, c.DefinitionId, c.Date, c.Start, c.End, Guid.NewGuid(), true, false, ShiftType.DAY);
        Assert.Equal(employee.Id, Solve(Problem([c], 2, [w])).Cells.Single().Employees.Single().UserId);
    }
    [Fact]
    public void No_solution_with_zero_time_is_reported_as_timeout_and_cancellation_propagates()
    {
        var p = Problem([Cell(1)]);
        Assert.Equal("TIMEOUT", new ScheduleOptimizer().Solve(p, 0, CancellationToken.None).Status);
        Assert.Throws<OperationCanceledException>(() => new ScheduleOptimizer().Solve(p, 10, new CancellationToken(true)));
    }
    [Theory]
    [InlineData(2026, 3, 26, 11)]
    [InlineData(2026, 10, 24, 13)]
    public void Jerusalem_dst_uses_wall_clock_boundaries(int year, int month, int day, int hours)
    {
        var times = ShiftClock.Boundaries(new(year, month, day), ShiftType.NIGHT);
        Assert.Equal(hours, (times.EndsAt - times.StartsAt).TotalHours);
    }
    [Fact]
    public void Save_revalidation_rejects_tampered_assignments()
    {
        var p = Problem([Cell(1)]); var r = Solve(p);
        var cell = r.Cells[0] with { Employees = [new(Guid.NewGuid(), "לא זכאי", false, false)] };
        Assert.Throws<ApiException>(() => GeneratedDraftService.Validate(p, [cell]));
    }
    [Fact]
    public void Fairness_accounts_for_differing_opportunity_capacity()
    {
        var cells = Enumerable.Range(0, 4).Select(i => Cell(i + 1, i)).ToArray();
        var blocks = cells.Skip(1).Select(c => new Preference(Employee(2).Id, c.Date, ShiftType.DAY, PreferenceType.BLOCKED)).ToArray();
        var r = Solve(Problem(cells, 2, preferences: blocks));
        Assert.Equal(4, r.Cells.Sum(c => c.Employees.Length));
        Assert.Equal(Employee(2).Id, r.Cells[0].Employees.Single().UserId);
        Assert.Equal(3, r.Cells.SelectMany(c => c.Employees).Count(e => e.UserId == Employee(1).Id));
    }

    [Fact]
    public void Expert_coverage_strictly_precedes_two_ordinary_positions()
    {
        var expert = Cell(1, range: ShiftTimeRange.NIGHT) with { RequiresShiftLeadQualification = true };
        var p = Problem([expert, Cell(2), Cell(3, 1)], 1);
        p = p with { Employees = [p.Employees[0] with { IsShiftLeadQualified = true }] };
        var result = Solve(p);
        Assert.Equal(5, result.ProvenObjectives);
        Assert.Single(result.Cells[0].Employees);
        Assert.Equal(1, result.Cells.Sum(c => c.Employees.Length));
        Assert.False(result.Cells[0].Employees[0].IsShiftLead);
        GeneratedDraftService.Validate(p, result.Cells);
    }

    [Fact]
    public void Qualification_limits_expert_staffing_but_allows_ordinary_work()
    {
        var expert = Cell(1, required: 2) with { RequiresShiftLeadQualification = true };
        var p = Problem([expert, Cell(2), Cell(3, 2)], 2);
        p = p with { Employees = [p.Employees[0] with { IsShiftLeadQualified = true }, p.Employees[1]] };
        var result = Solve(p);
        Assert.Single(result.Cells[0].Employees);
        Assert.Equal(1, result.Cells[0].Shortage);
        Assert.Equal(p.Employees[1].Id, result.Cells[1].Employees.Single().UserId);
        Assert.Single(result.Cells[2].Employees);
        GeneratedDraftService.Validate(p, result.Cells);
        var forged = result.Cells.Select(c => c.DefinitionId == expert.DefinitionId
            ? c with { Employees = [new(p.Employees[1].Id, "unqualified", false, false)] } : c).ToArray();
        Assert.Throws<ApiException>(() => GeneratedDraftService.Validate(p, forged));
    }

    [Fact]
    public void Expert_shortage_respects_blocks_and_protected_qualification()
    {
        var c = Cell(1, required: 2) with { RequiresShiftLeadQualification = true };
        var p = Problem([c], 1, preferences: [new(Employee(1).Id, Date, ShiftType.DAY, PreferenceType.BLOCKED)]);
        p = p with { Employees = [p.Employees[0] with { IsShiftLeadQualified = true }] };
        Assert.Equal(2, Solve(p).Cells[0].Shortage);
        var w = new Work(Guid.NewGuid(), Employee(1).Id, c.DefinitionId, c.Date, c.Start, c.End, Guid.NewGuid(), false, true, ShiftType.DAY);
        var protectedProblem = Problem([c], 1, [w]);
        Assert.Throws<ApiException>(() => Solve(protectedProblem));
        protectedProblem = protectedProblem with { Employees = [protectedProblem.Employees[0] with { IsShiftLeadQualified = true }] };
        var result = Solve(protectedProblem);
        Assert.True(result.Cells[0].Employees.Single().IsShiftLead);
        GeneratedDraftService.Validate(protectedProblem, result.Cells);
    }

}
