using ShiftManager.Api;
using ShiftManager.Domain;
using ShiftManager.Services;
using ShiftManager.Services.AutomaticScheduling;
using static ShiftManager.Tests.SchedulingValidationScenarios;

namespace ShiftManager.Tests;

public sealed class SchedulingValidationTests
{
    private static readonly DateOnly Sunday = new(2026, 11, 1);

    [Theory]
    [InlineData("unknown", "ELIGIBILITY")]
    [InlineData("leader", "ELIGIBILITY")]
    [InlineData("unqualified", "QUALIFICATION")]
    [InlineData("manual", "MANUAL_ONLY")]
    [InlineData("zero", "CAPACITY")]
    [InlineData("duplicate", "DUPLICATE")]
    [InlineData("blocked", "AVAILABILITY")]
    [InlineData("absence", "ABSENCE")]
    [InlineData("recurring", "ABSENCE")]
    [InlineData("partial", "ABSENCE")]
    [InlineData("touching", "OVERLAP_OR_TOUCH")]
    [InlineData("anchor", "ANCHOR_DUPLICATE")]
    [InlineData("nights", "WEEKLY_NIGHT")]
    [InlineData("six-nine", "WEEKLY_SIX_NINE")]
    [InlineData("weekends", "MONTHLY_WEEKEND")]
    [InlineData("pair", "ON_CALL_PAIR")]
    [InlineData("saturday", "APPLICABILITY")]
    [InlineData("missing", "CELL_SET")]
    [InlineData("metadata", "CELL_METADATA")]
    [InlineData("protected", "PROTECTED")]
    public void Independent_oracle_detects_deliberately_invalid_rosters(string mutation, string expected)
    {
        var employee = Employee(1, false);
        var cells = new[] { Cell(2, Sunday) };
        var p = Problem(cells, [employee]);
        switch (mutation)
        {
            case "unknown": p = p with { Employees = [] }; break;
            case "leader": p = p with { Employees = [employee with { IsTeamLeader = true }] }; break;
            case "unqualified": cells = [cells[0] with { RequiresShiftLeadQualification = true }]; break;
            case "manual": cells = [cells[0] with { AssignmentMode = AssignmentMode.ManualOnly }]; break;
            case "zero": cells = [cells[0] with { Required = 0 }]; break;
            case "blocked": p = p with { Preferences = [new(employee.Id, Sunday, ShiftType.DAY, PreferenceType.BLOCKED)] }; break;
            case "absence": case "recurring": case "partial":
                p = p with { ApprovedAbsences = [new(Id(100), employee.Id, Sunday, null, mutation == "recurring", [0], mutation != "partial",
                    mutation == "partial" ? new(12, 0) : null, mutation == "partial" ? new(13, 0) : null)] }; break;
            case "touching": cells = [cells[0], Cell(5, Sunday, ShiftTimeRange.NIGHT)]; break;
            case "anchor": cells = [Cell(1, Sunday, ShiftTimeRange.H06_09), Cell(26, Sunday, ShiftTimeRange.H15_23)]; break;
            case "nights": cells = new[] { 0, 2, 4 }.Select(i => Cell(5, Sunday.AddDays(i), ShiftTimeRange.NIGHT)).ToArray(); break;
            case "six-nine": cells = new[] { 0, 1 }.Select(i => Cell(1, Sunday.AddDays(i), ShiftTimeRange.H06_09, semantic: "SIX_NINE")).ToArray(); break;
            case "weekends": cells = new[] { 5, 12, 19 }.Select(i => Cell(2, Sunday.AddDays(i))).ToArray(); break;
            case "pair": cells = [Cell(16, Sunday.AddDays(5), semantic: "WEEKEND_ON_CALL")]; break;
            case "saturday": cells = [Cell(8, Sunday.AddDays(6))]; break;
            case "protected": p = p with { Work = [Work(cells[0], employee)] }; break;
        }
        p = p with { Cells = cells };
        var forged = Assign(cells, employee);
        if (mutation == "duplicate") forged[0] = forged[0] with { Employees = [forged[0].Employees[0], forged[0].Employees[0]] };
        if (mutation == "missing") forged = [];
        if (mutation == "metadata") forged[0] = forged[0] with { EndsAt = forged[0].EndsAt.AddMinutes(1) };
        Assert.Contains(expected, IndependentScheduleValidator.Check(p, forged));
    }

    [Theory]
    [InlineData(2026, 3, 26, 11)] [InlineData(2026, 10, 24, 13)]
    public void Solver_and_independent_oracle_respect_dst_overnight_and_partial_absences(int year, int month, int day, int elapsedHours)
    {
        var date = new DateOnly(year, month, day);
        var cell = Cell(5, date, ShiftTimeRange.NIGHT);
        Assert.Equal(elapsedHours, (cell.End - cell.Start).TotalHours);
        Assert.Equal(IndependentScheduleValidator.Utc(date, 19), cell.Start);
        Assert.Equal(IndependentScheduleValidator.Utc(date.AddDays(1), 7), cell.End);
        var e = Employee(1);
        var p = Problem([cell], [e]);
        var r = new ScheduleOptimizer().Solve(p, 5, default);
        Assert.Single(r.Cells[0].Employees);
        Assert.Empty(IndependentScheduleValidator.Check(p, r.Cells));
        var absenceDate = date.AddDays(1);
        var absence = new AbsenceRule(Id(100), e.Id, absenceDate, absenceDate, false, [], false, new(1, 0), new(4, 0));
        p = p with { ApprovedAbsences = [absence] };
        Assert.Contains("ABSENCE", IndependentScheduleValidator.Check(p, r.Cells));
        r = new ScheduleOptimizer().Solve(p, 5, default);
        Assert.Empty(r.Cells[0].Employees);
        Assert.Empty(IndependentScheduleValidator.Check(p, r.Cells));
    }

    [Theory]
    [InlineData(6, 7, false)] [InlineData(18, 20, true)] [InlineData(19, 20, false)]
    public void Partial_absence_boundaries_are_half_open(int start, int end, bool blocked)
    {
        var c = Cell(2, Sunday); var e = Employee(1);
        var absence = new AbsenceRule(Id(100), e.Id, Sunday, Sunday, false, [], false, new(start, 0), new(end, 0));
        var p = Problem([c], [e], absences: [absence]);
        var result = new ScheduleOptimizer().Solve(p, 5, default);
        Assert.Equal(blocked ? 0 : 1, result.Cells[0].Employees.Length);
        Assert.Empty(IndependentScheduleValidator.Check(p, result.Cells));
    }

    [Fact]
    public void Pending_absence_changes_do_not_weaken_approved_recurring_rule()
    {
        var e = Employee(1);
        var p = Problem([Cell(2, Sunday), Cell(2, Sunday.AddDays(1))], [e], absences: [
            new(Id(100), e.Id, Sunday, null, true, [0], false, new(8, 0), new(16, 0))]);
        p = p with { PendingAbsences = [new(Id(101), e.Id, Sunday.AddDays(1), Sunday.AddDays(1), false, [], true, null, null)] };
        var r = new ScheduleOptimizer().Solve(p, 5, default);
        Assert.Empty(r.Cells[0].Employees); Assert.Single(r.Cells[1].Employees);
        Assert.Empty(IndependentScheduleValidator.Check(p, r.Cells));
    }

    [Theory]
    [InlineData("NIGHT")] [InlineData("SIX_NINE")] [InlineData("TOUCHING")]
    public void Previous_year_commitments_are_hard_obstacles(string rule)
    {
        var date = new DateOnly(2027, 1, 1); var e = Employee(1);
        var range = rule is "NIGHT" or "TOUCHING" ? ShiftTimeRange.NIGHT : ShiftTimeRange.H06_09;
        var semantic = rule == "SIX_NINE" ? "SIX_NINE" : "ORDINARY";
        var previous = new[] { 28, 30 }.Select(d => Work(Cell(2, new(2026, 12, d), range, semantic: semantic), e, d)).ToArray();
        if (rule == "SIX_NINE") previous = previous.Take(1).ToArray();
        if (rule == "TOUCHING") previous = [Work(Cell(5, new(2026, 12, 31), range), e)];
        var c = Cell(2, date, rule == "TOUCHING" ? ShiftTimeRange.DAY : range, semantic: semantic);
        var p = Problem([c], [e], previous);
        var result = new ScheduleOptimizer().Solve(p, 5, default);
        Assert.Empty(result.Cells[0].Employees);
        Assert.Empty(IndependentScheduleValidator.Check(p, result.Cells));
    }

    [Fact]
    public void Year_boundary_on_call_pair_is_one_Friday_month_allocation_and_keeps_same_employee()
    {
        var e = Employee(1); var date = new DateOnly(2027, 1, 1); // Friday
        var friday = Cell(16, date, semantic: "WEEKEND_ON_CALL");
        var saturday = Cell(16, date.AddDays(1), semantic: "WEEKEND_ON_CALL");
        var lastYear = Work(Cell(2, new(2026, 12, 25)), e);
        var p = Problem([friday, saturday, Cell(8, date.AddDays(7)), Cell(8, date.AddDays(14))], [e], [lastYear]);
        var result = new ScheduleOptimizer().Solve(p, 5, default);
        Assert.Empty(IndependentScheduleValidator.Check(p, result.Cells));
        Assert.Single(result.Cells[0].Employees); Assert.Single(result.Cells[1].Employees);
        Assert.Equal(3, result.Cells.Sum(c => c.Employees.Length));
        Assert.Equal(result.Cells[0].Employees.Select(x => x.UserId), result.Cells[1].Employees.Select(x => x.UserId));
        Assert.Equal(2, SchedulingStressTests.Metrics(p, result)[0].WeekendAllocations);
    }

    [Fact]
    public void Inconsistent_protected_work_is_rejected_without_mutating_input()
    {
        var e = Employee(1); var c = Cell(2, Sunday, required: 0); var w = Work(c, e);
        var p = Problem([c], [e], [w]);
        var error = Assert.Throws<ApiException>(() => new ScheduleOptimizer().Solve(p, 5, default));
        Assert.Equal("STAFFING_EXCEEDED", error.Code);
        Assert.Equal(w, p.Work.Single()); Assert.False(p.Work[0].Replaceable);
    }

    [Theory]
    [InlineData(12)] [InlineData(13)] [InlineData(14)]
    public void Manual_only_work_is_retained_but_remains_subject_to_approved_absences(int slot)
    {
        var c = Cell(slot, Sunday, mode: AssignmentMode.ManualOnly); var e = Employee(1);
        var p = Problem([c], [e]);
        Assert.Empty(new ScheduleOptimizer().Solve(p, 5, default).Cells[0].Employees);
        p = p with { Work = [Work(c, e)] };
        var result = new ScheduleOptimizer().Solve(p, 5, default);
        Assert.True(result.Cells[0].Employees.Single().Protected);
        Assert.Empty(IndependentScheduleValidator.Check(p, result.Cells));
        p = p with { ApprovedAbsences = [new(Id(100), e.Id, Sunday, Sunday, false, [], true, null, null)] };
        Assert.Equal("APPROVED_ABSENCE", Assert.Throws<ApiException>(() => new ScheduleOptimizer().Solve(p, 5, default)).Code);
    }

    [Fact]
    public void Row_26_has_ordinary_weekday_behavior_with_no_added_special_restriction()
    {
        var cells = new[] { Cell(26, Sunday), Cell(26, Sunday.AddDays(1)) };
        var p = Problem(cells);
        var result = new ScheduleOptimizer().Solve(p, 5, default);
        Assert.All(result.Cells, c => Assert.Single(c.Employees));
        Assert.Empty(IndependentScheduleValidator.Check(p, result.Cells));
    }

    [Fact]
    public void Priority_coverage_is_pinned_before_conflicting_preferences_and_regeneration_stability()
    {
        var e = Employee(1);
        var priority = Cell(2, Sunday, ShiftTimeRange.H07_13) with { SchedulingPriority = SchedulingPriority.Critical };
        var other = Cell(26, Sunday, ShiftTimeRange.H15_23);
        // Separate intervals still share one canonical DAY membership. Only the
        // lower-priority row overlaps the preferred NIGHT anchor and old choice.
        var p = Problem([other, priority], [e], [Work(other, e, replaceable: true)],
            [new(e.Id, Sunday, ShiftType.NIGHT, PreferenceType.PREFERRED)]);
        var result = new ScheduleOptimizer().Solve(p, 5, default);
        Assert.Equal("OPTIMAL", result.Status); Assert.Equal(ScheduleOptimizer.ObjectiveCount, result.ProvenObjectives);
        Assert.Empty(result.Cells[0].Employees); Assert.Single(result.Cells[1].Employees);
        Assert.Empty(IndependentScheduleValidator.Check(p, result.Cells));
    }

    [Fact]
    public void Infeasible_protected_on_call_pair_is_rejected_when_its_mate_is_blocked()
    {
        var e = Employee(1); var friday = Sunday.AddDays(5);
        var cells = new[] { Cell(16, friday, semantic: "WEEKEND_ON_CALL"), Cell(16, friday.AddDays(1), semantic: "WEEKEND_ON_CALL") };
        var retained = Work(cells[0], e);
        var p = Problem(cells, [e], [retained], [new(e.Id, friday.AddDays(1), ShiftType.DAY, PreferenceType.BLOCKED)]);
        // Fixed Friday presence=1 and blocked Saturday presence=0 make the CP-SAT
        // pair equality infeasible, after all fixed-work preconditions have passed.
        var exception = Assert.Throws<ApiException>(() => new ScheduleOptimizer().Solve(p, 5, default));
        Assert.Equal("EXISTING_CONSTRAINT_CONFLICT", exception.Code);
        Assert.Equal(retained, p.Work.Single());
    }
}
