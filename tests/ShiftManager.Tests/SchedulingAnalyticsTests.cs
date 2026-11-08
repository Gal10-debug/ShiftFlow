using ShiftManager.Api;
using ShiftManager.Domain;
using ShiftManager.Services;

namespace ShiftManager.Tests;

public sealed class SchedulingAnalyticsTests
{
    private static readonly Guid Employee = Guid.NewGuid();
    private static SnapshotCell Cell(int day, ShiftTimeRange range = ShiftTimeRange.DAY, int slot = 1, string semantic = "ORDINARY")
    {
        var date = new DateOnly(2026, 10, day); var t = ShiftTimeRanges.Boundaries(date, range);
        return new(Guid.Parse($"40000000-0000-0000-0000-{slot:000000000000}"), date, "Frozen name", slot,
            ShiftTimeRanges.Classification(range).ToString(), range.ToString(), t.StartsAt, t.EndsAt, 2, false,
            [new(Guid.NewGuid(), Employee, "Employee", false)], semantic);
    }
    private static ScheduleSnapshot Snapshot(params SnapshotCell[] cells) => new(2026, 10, cells, []);
    [Fact]
    public void Monthly_day_night_weekend_special_and_unknown_totals_reuse_phase8a()
    {
        var snapshot = Snapshot(Cell(1), Cell(2, ShiftTimeRange.NIGHT), Cell(3, slot: 16, semantic: "WEEKEND_ON_CALL"),
            Cell(4, slot: 14), Cell(5, ShiftTimeRange.H06_09, 3, "SIX_NINE"));
        var result = SchedulingAnalyticsCalculator.Employees(snapshot)[Employee];
        Assert.Equal(5, result.TotalAssignedShifts); Assert.Equal(1620, result.KnownPlannedMinutes);
        Assert.Equal(2, result.UnknownDurationAssignments); Assert.Null(result.TotalPlannedMinutes);
        Assert.Equal(2, result.DayShiftCount); Assert.Equal(1, result.NightShiftCount);
        Assert.Equal(2, result.WeekendShiftCount); Assert.Equal(3, result.SpecialDutyAssignments);
        Assert.Equal(PlannedWorkHoursCalculator.Calculate(snapshot, Employee, 2026, 10).Summary.KnownPlannedMinutes, result.KnownPlannedMinutes);
    }
    [Fact]
    public void Annual_totals_are_additive_for_partial_year_and_preserve_unknowns()
    {
        var october = SchedulingAnalyticsCalculator.Employees(Snapshot(Cell(31, ShiftTimeRange.NIGHT)))[Employee];
        var unknown = SchedulingAnalyticsCalculator.Employees(Snapshot(Cell(2, slot: 16, semantic: "WEEKEND_ON_CALL")))[Employee];
        var total = SchedulingAnalyticsCalculator.Sum([october, unknown, SchedulingAnalyticsCalculator.Empty]);
        Assert.Equal(2, total.TotalAssignedShifts); Assert.Equal(720, total.KnownPlannedMinutes);
        Assert.Equal(1, total.UnknownDurationAssignments); Assert.False(total.IsComplete);
        Assert.Equal(total.TotalAssignedShifts, total.ByShiftType.Sum(t => t.AssignedShifts));
    }
    [Fact]
    public void Duplicate_memberships_count_once_and_conflicting_durations_remain_unknown()
    {
        var cell = Cell(1);
        var result = SchedulingAnalyticsCalculator.Employees(Snapshot(cell, cell))[Employee];
        Assert.Equal(1, result.TotalAssignedShifts); Assert.Equal(720, result.TotalPlannedMinutes);
        result = SchedulingAnalyticsCalculator.Employees(Snapshot(cell, cell with { EndsAt = cell.EndsAt.AddHours(-1) }))[Employee];
        Assert.Equal(1, result.TotalAssignedShifts); Assert.Equal(1, result.UnknownDurationAssignments);
    }
    [Fact]
    public void Coverage_deduplicates_cells_and_assignments_caps_overstaffing_and_excludes_manual_nonapplicable_legacy()
    {
        var cell = Cell(1); var over = Cell(2) with { RequiredEmployees = 1, Employees = [new(Guid.NewGuid(), Employee, "A", false), new(Guid.NewGuid(), Guid.NewGuid(), "B", false)] };
        var snapshot = Snapshot(cell, cell, over, Cell(3) with { AssignmentMode = "ManualOnly" }, Cell(4) with { Applicable = false }, Cell(5) with { Employees = [] });
        var coverage = SchedulingAnalyticsCalculator.Coverage(snapshot);
        Assert.Equal(5, coverage.RequiredPositions); Assert.Equal(2, coverage.FilledPositions); Assert.Equal(3, coverage.UnfilledPositions);
        Assert.Equal(40m, coverage.CoveragePercentage); Assert.Single(coverage.ByShiftType);
        Assert.Equal(3, coverage.ByShiftType[0].UnfilledPositions);
    }
    [Fact]
    public void Empty_month_and_no_required_positions_are_distinct_from_missing_publication()
    {
        Assert.Empty(SchedulingAnalyticsCalculator.Employees(Snapshot()));
        var totals = SchedulingAnalyticsCalculator.Sum([]); Assert.True(totals.IsComplete); Assert.Equal(0, totals.TotalPlannedMinutes);
        var coverage = SchedulingAnalyticsCalculator.Coverage(Snapshot()); Assert.Null(coverage.CoveragePercentage);
        Assert.Null(SchedulingAnalyticsCalculator.SumCoverage([]));
        Assert.NotNull(SchedulingAnalyticsCalculator.SumCoverage([coverage]));
    }
    [Fact]
    public void Distribution_includes_zero_eligible_workers_and_uses_explicit_denominators()
    {
        var total = SchedulingAnalyticsCalculator.Employees(Snapshot(Cell(1), Cell(2)))[Employee];
        var distribution = SchedulingAnalyticsCalculator.Distribution([new(Employee, "A", null, total), new(Guid.NewGuid(), "B", null, SchedulingAnalyticsCalculator.Empty)]);
        Assert.Equal(1, distribution.ScheduledEmployees); Assert.Equal(2, distribution.IncludedEmployees);
        Assert.Equal(0, distribution.MinimumAssignedShifts); Assert.Equal(2, distribution.MaximumAssignedShifts);
        Assert.Equal(1m, distribution.AverageAssignedShifts); Assert.Equal(2m, distribution.AverageShiftsPerScheduledEmployee);
        Assert.Equal(new[] { 0, 2 }, distribution.ShiftCounts.Select(c => c.AssignedShifts));
    }
    [Theory]
    [InlineData(8)] [InlineData(12)] [InlineData(13)]
    public void Special_operational_rows_keep_frozen_duration(int slot)
    {
        var result = SchedulingAnalyticsCalculator.Employees(Snapshot(Cell(1, ShiftTimeRange.H07_13, slot)))[Employee];
        Assert.Equal(360, result.TotalPlannedMinutes); Assert.Equal(1, result.SpecialDutyAssignments);
    }
    [Fact]
    public void Annual_coverage_is_weighted_from_monthly_positions()
    {
        var one = SchedulingAnalyticsCalculator.Coverage(Snapshot(Cell(1)));
        var two = SchedulingAnalyticsCalculator.Coverage(Snapshot(Cell(2) with { RequiredEmployees = 3 }));
        var result = SchedulingAnalyticsCalculator.SumCoverage([one, null, two])!;
        Assert.Equal(5, result.RequiredPositions); Assert.Equal(2, result.FilledPositions); Assert.Equal(40m, result.CoveragePercentage);
    }
}
