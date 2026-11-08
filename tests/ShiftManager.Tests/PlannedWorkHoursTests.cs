using ShiftManager.Api;
using ShiftManager.Domain;
using ShiftManager.Services;

namespace ShiftManager.Tests;

public sealed class PlannedWorkHoursTests
{
    private static readonly Guid Employee = Guid.NewGuid();
    private static Guid Definition(int slot) => Guid.Parse($"40000000-0000-0000-0000-{slot:000000000000}");
    private static SnapshotCell Cell(DateOnly date, ShiftTimeRange range = ShiftTimeRange.DAY, int slot = 2,
        string semantic = "ORDINARY", bool lead = false, Guid? assignment = null)
    {
        var (start, end) = ShiftTimeRanges.Boundaries(date, range);
        return new(Definition(slot), date, "Published name", slot, ShiftTimeRanges.Classification(range).ToString(),
            range.ToString(), start, end, 1, lead, [new(assignment ?? Guid.NewGuid(), Employee, "Employee", lead)], semantic);
    }
    private static PersonalScheduleResponse Calculate(params SnapshotCell[] cells) =>
        PlannedWorkHoursCalculator.Calculate(new(2026, 10, cells, []), Employee, 2026, 10);

    [Theory]
    [InlineData(ShiftTimeRange.DAY, 720, false)]
    [InlineData(ShiftTimeRange.NIGHT, 720, true)]
    [InlineData(ShiftTimeRange.H06_09, 180, false)]
    [InlineData(ShiftTimeRange.H07_13, 360, false)]
    [InlineData(ShiftTimeRange.H07_15, 480, false)]
    [InlineData(ShiftTimeRange.H15_23, 480, false)]
    [InlineData(ShiftTimeRange.H23_07, 480, true)]
    public void Explicit_ranges_use_elapsed_minutes_and_identify_overnight(ShiftTimeRange range, int minutes, bool overnight)
    {
        var result = Calculate(Cell(new(2026, 10, 1), range));
        Assert.Equal(minutes, result.Summary.TotalPlannedMinutes);
        Assert.Equal(minutes / 60m, result.Summary.TotalPlannedHours);
        Assert.Equal(overnight, Assert.Single(result.Assignments).Overnight);
        Assert.Equal(range is ShiftTimeRange.NIGHT or ShiftTimeRange.H23_07 ? 1 : 0, result.Summary.NightShiftCount);
    }

    [Theory]
    [InlineData(2026, 3, 26, 660)]
    [InlineData(2026, 10, 24, 780)]
    public void Jerusalem_DST_nights_resolve_both_boundaries(int year, int month, int day, int minutes)
    {
        var result = PlannedWorkHoursCalculator.Calculate(new(year, month, [Cell(new(year, month, day), ShiftTimeRange.NIGHT)], []), Employee, year, month);
        Assert.Equal(minutes, result.Summary.TotalPlannedMinutes);
    }

    [Fact]
    public void Month_boundary_keeps_entire_night_on_assignment_date_and_excludes_other_months()
    {
        var result = Calculate(Cell(new(2026, 9, 30)), Cell(new(2026, 10, 31), ShiftTimeRange.NIGHT), Cell(new(2026, 11, 1)));
        Assert.Single(result.Assignments); Assert.Equal(720, result.Summary.TotalPlannedMinutes);
        Assert.Equal("ASSIGNMENT_DATE", result.AttributionPolicy);
    }

    [Theory]
    [InlineData(2024, 29)]
    [InlineData(2025, 28)]
    public void February_respects_leap_years(int year, int lastDay)
    {
        var result = PlannedWorkHoursCalculator.Calculate(new(year, 2, [Cell(new(year, 2, lastDay), ShiftTimeRange.NIGHT)], []), Employee, year, 2);
        Assert.Single(result.Assignments); Assert.Equal(720, result.Summary.TotalPlannedMinutes);
    }

    [Fact]
    public void Multiple_types_sum_precisely_and_unassigned_or_other_employees_do_not_count()
    {
        var ignored = Cell(new(2026, 10, 3)) with { Employees = [] };
        var other = Cell(new(2026, 10, 4)) with { Employees = [new(Guid.NewGuid(), Guid.NewGuid(), "Other", false)] };
        var result = Calculate(Cell(new(2026, 10, 1)), Cell(new(2026, 10, 2), ShiftTimeRange.H06_09, 1, "SIX_NINE"), ignored, other);
        Assert.Equal(2, result.Summary.TotalAssignedShifts); Assert.Equal(900, result.Summary.TotalPlannedMinutes);
        Assert.Equal(2, result.Summary.ByShiftType.Length); Assert.Equal(900, result.Summary.ByShiftType.Sum(t => t.KnownPlannedMinutes));
    }

    [Fact]
    public void Duplicate_ids_are_counted_once_and_conflicting_intervals_are_explicitly_unknown()
    {
        var cell = Cell(new(2026, 10, 1));
        Assert.Equal(720, Calculate(cell, cell).Summary.TotalPlannedMinutes);
        var conflict = Calculate(cell, cell with { EndsAt = cell.EndsAt.AddHours(-1) });
        Assert.Equal(1, conflict.Summary.TotalAssignedShifts); Assert.Null(conflict.Summary.TotalPlannedMinutes);
        Assert.Equal("CONFLICTING_DUPLICATE", conflict.Assignments[0].DurationUnavailableReason);
    }

    [Theory]
    [InlineData(8, "REHABILITATION")]
    [InlineData(12, "EARLY_END")]
    [InlineData(13, "EARLY_END")]
    public void Operational_and_early_end_rows_use_frozen_times_never_labels(int slot, string category)
    {
        var cell = Cell(new(2026, 10, 1), ShiftTimeRange.H07_13, slot) with { Name = "עד 16" };
        var result = Calculate(cell);
        Assert.Equal(360, result.Summary.TotalPlannedMinutes); Assert.Equal(category, result.Assignments[0].Category);
    }

    [Fact]
    public void On_call_pair_is_one_represented_duty_with_two_memberships_and_no_invented_hours()
    {
        var result = Calculate(Cell(new(2026, 10, 2), slot: 16, semantic: "WEEKEND_ON_CALL"),
            Cell(new(2026, 10, 3), slot: 16, semantic: "WEEKEND_ON_CALL"));
        Assert.Equal(2, result.Summary.TotalAssignedShifts); Assert.Equal(1, result.Summary.OnCallDutyCount);
        Assert.Equal(2, result.Summary.UnknownDurationAssignments); Assert.Equal(0, result.Summary.DayShiftCount);
        Assert.Null(result.Summary.TotalPlannedHours); Assert.Equal(0, result.Summary.KnownPlannedMinutes);
        Assert.All(result.Assignments, a => Assert.Equal("ON_CALL_WORK_DURATION_UNDEFINED", a.DurationUnavailableReason));
    }

    [Fact]
    public void Cross_month_on_call_does_not_import_another_publication_or_assign_work_hours()
    {
        foreach (var date in new[] { new DateOnly(2027, 4, 30), new DateOnly(2027, 5, 1) })
        {
            var result = PlannedWorkHoursCalculator.Calculate(new(date.Year, date.Month,
                [Cell(date, slot: 16, semantic: "WEEKEND_ON_CALL")], []), Employee, date.Year, date.Month);
            Assert.Single(result.Assignments); Assert.Equal(1, result.Summary.OnCallDutyCount);
            Assert.Null(result.Summary.TotalPlannedMinutes);
        }
    }

    [Fact]
    public void Evening_availability_remains_unknown_and_mixed_totals_are_incomplete()
    {
        var result = Calculate(Cell(new(2026, 10, 1)), Cell(new(2026, 10, 2), slot: 14));
        Assert.Equal(720, result.Summary.KnownPlannedMinutes); Assert.Equal(12m, result.Summary.KnownPlannedHours);
        Assert.Equal(1, result.Summary.UnknownDurationAssignments); Assert.False(result.Summary.IsComplete);
        Assert.Null(result.Summary.TotalPlannedMinutes); Assert.Equal(1, result.Summary.DayShiftCount);
        Assert.Equal("AVAILABILITY_WORK_DURATION_UNDEFINED", result.Assignments[1].DurationUnavailableReason);
    }

    [Fact]
    public void Qualifications_never_add_work_minutes()
    {
        var result = Calculate(Cell(new(2026, 10, 1), lead: true));
        Assert.Equal(720, result.Summary.TotalPlannedMinutes); Assert.Equal(1, result.Summary.TotalAssignedShifts);
    }

    [Theory]
    [InlineData("reversed")]
    [InlineData("missing")]
    [InlineData("unspecified")]
    [InlineData("seconds")]
    [InlineData("wrong-date")]
    public void Unreliable_intervals_are_unknown(string problem)
    {
        var cell = Cell(new(2026, 10, 1));
        cell = problem switch {
            "reversed" => cell with { EndsAt = cell.StartsAt },
            "missing" => cell with { StartsAt = default },
            "unspecified" => cell with { StartsAt = DateTime.SpecifyKind(cell.StartsAt, DateTimeKind.Unspecified) },
            "seconds" => cell with { EndsAt = cell.EndsAt.AddSeconds(1) },
            _ => cell with { StartsAt = cell.StartsAt.AddDays(-1) }
        };
        var result = Calculate(cell);
        Assert.False(result.Summary.IsComplete); Assert.Null(result.Assignments[0].PlannedMinutes);
        Assert.Equal("INVALID_INTERVAL", result.Assignments[0].DurationUnavailableReason);
    }

    [Fact]
    public void Legacy_work_uses_its_own_frozen_interval_and_empty_month_is_complete_zero()
    {
        var c = Cell(new(2026, 10, 1)); var e = c.Employees[0];
        var legacy = new LegacyWork(e.AssignmentId, Employee, "Historical", c.Date, c.Period, c.StartsAt, c.EndsAt, false);
        var result = PlannedWorkHoursCalculator.Calculate(new(2026, 10, [], [legacy]), Employee, 2026, 10);
        Assert.Equal(720, result.Summary.TotalPlannedMinutes);
        Assert.Equal("יום", result.Assignments[0].Name);
        var empty = Calculate(); Assert.True(empty.Summary.IsComplete); Assert.Equal(0, empty.Summary.TotalPlannedMinutes);
    }

    [Theory]
    [InlineData(2026, 0)]
    [InlineData(2026, 13)]
    [InlineData(0, 1)]
    [InlineData(9999, 12)]
    public void Unsupported_reporting_periods_are_rejected(int year, int month) =>
        Assert.ThrowsAny<Exception>(() => PlannedWorkHoursCalculator.Calculate(new(year, month, [], []), Employee, year, month));

    [Fact]
    public void A_different_snapshot_month_cannot_be_mixed_into_the_report() =>
        Assert.Throws<ArgumentException>(() => PlannedWorkHoursCalculator.Calculate(new(2026, 9, [], []), Employee, 2026, 10));
}
