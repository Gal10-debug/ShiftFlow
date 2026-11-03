using ShiftManager.Api;
using ShiftManager.Domain;
using ShiftManager.Services;
using ShiftManager.Services.AutomaticScheduling;
namespace ShiftManager.Tests;
public sealed class AbsenceRulesTests
{
    private static AbsenceRule Rule(DateOnly start, DateOnly? end, bool recurring = false, int[]? days = null,
        bool full = true, TimeOnly? from = null, TimeOnly? through = null) => new(Guid.NewGuid(), Guid.NewGuid(), start, end, recurring, days ?? [], full, from, through);
    private static DateTime Utc(int y, int m, int d, int h = 0, int min = 0, bool end = false) => AbsenceRules.Utc(new(y, m, d, h, min, 0), end);
    [Fact]
    public void Inclusive_dates_use_half_open_actual_intervals_and_include_previous_month_overnight()
    {
        var rule = Rule(new(2026, 11, 1), new(2026, 11, 2));
        Assert.True(AbsenceRules.Overlaps(rule, Utc(2026, 10, 31, 19), Utc(2026, 11, 1, 7)));
        Assert.True(AbsenceRules.Overlaps(rule, Utc(2026, 11, 2, 23), Utc(2026, 11, 3, 7)));
        Assert.False(AbsenceRules.Overlaps(rule, Utc(2026, 11, 3), Utc(2026, 11, 3, 7)));
        Assert.False(AbsenceRules.Overlaps(rule, Utc(2026, 10, 31, 19), Utc(2026, 11, 1)));
    }
    [Fact]
    public void Recurrence_is_evaluated_in_future_months_and_partial_ranges_can_cross_midnight()
    {
        var rule = Rule(new(2026, 11, 1), null, true, [2], false, new(8, 0), new(16, 0));
        var tuesday = new DateOnly(2031, 6, 1); while (tuesday.DayOfWeek != DayOfWeek.Tuesday) tuesday = tuesday.AddDays(1);
        var start = AbsenceRules.Utc(tuesday.ToDateTime(new(15, 0)), false);
        Assert.True(AbsenceRules.Overlaps(rule, start, start.AddHours(2)));
        Assert.False(AbsenceRules.Overlaps(rule, start.AddDays(1), start.AddDays(1).AddHours(2)));
        var overnight = Rule(new(2026, 11, 1), new(2026, 11, 1), full: false, from: new(23, 0), through: new(3, 0));
        Assert.True(AbsenceRules.Overlaps(overnight, Utc(2026, 11, 2, 2), Utc(2026, 11, 2, 4)));
        Assert.False(AbsenceRules.Overlaps(overnight, Utc(2026, 11, 2, 3), Utc(2026, 11, 2, 4)));
    }
    [Theory]
    [InlineData(2026, 3, 27, 23)]
    [InlineData(2026, 10, 25, 25)]
    public void Israel_full_days_follow_DST_elapsed_hours(int y, int m, int d, int hours)
    {
        var day = new DateOnly(y, m, d);
        var from = AbsenceRules.Utc(day.ToDateTime(TimeOnly.MinValue), false);
        var through = AbsenceRules.Utc(day.AddDays(1).ToDateTime(TimeOnly.MinValue), true);
        Assert.Equal(TimeSpan.FromHours(hours), through - from);
        Assert.True(AbsenceRules.Overlaps(Rule(day, day), through.AddTicks(-1), through));
    }
    [Fact]
    public void Ambiguous_partial_time_covers_both_occurrences_and_gap_is_deterministic()
    {
        var early = Utc(2026, 10, 25, 1, 30); var late = Utc(2026, 10, 25, 1, 30, true);
        Assert.Equal(TimeSpan.FromHours(1), late - early);
        var rule = Rule(new(2026, 10, 25), new(2026, 10, 25), full: false, from: new(1, 15), through: new(1, 45));
        Assert.True(AbsenceRules.Overlaps(rule, early, early.AddMinutes(1)));
        Assert.True(AbsenceRules.Overlaps(rule, late, late.AddMinutes(1)));
        Assert.Equal(Utc(2026, 3, 27, 3), Utc(2026, 3, 27, 2, 30));
    }
    [Fact]
    public void Pending_requests_warn_only_while_approved_requests_block_solver_and_preview()
    {
        var e = new Employee(Guid.NewGuid(), "עובד"); var d = new DateOnly(2026, 11, 1); var t = ShiftClock.Boundaries(d, ShiftType.DAY);
        var c = new CandidateCell(Guid.Parse("40000000-0000-0000-0000-000000000002"), d, t.StartsAt, t.EndsAt, 1, 1, ShiftType.DAY);
        var rule = Rule(d, d) with { UserId = e.Id };
        var p = new SchedulingProblem(2026, 11, "", [c], [e], [], [], PendingAbsences: [rule]);
        var optimizer = new ScheduleOptimizer(); var pending = optimizer.Solve(p, 5, default);
        Assert.Single(pending.Cells[0].Employees);
        p = p with { ApprovedAbsences = [rule] };
        Assert.Empty(optimizer.Solve(p, 5, default).Cells[0].Employees);
        Assert.Throws<ApiException>(() => GeneratedDraftService.Validate(p, pending.Cells));
    }
}
