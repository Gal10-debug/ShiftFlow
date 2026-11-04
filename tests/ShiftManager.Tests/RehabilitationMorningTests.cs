using ShiftManager.Domain;
using ShiftManager.Services.AutomaticScheduling;
namespace ShiftManager.Tests;

public sealed class RehabilitationMorningTests
{
    [Theory]
    [InlineData(2026, 10, 2)]
    [InlineData(2027, 4, 30)]
    [InlineData(2026, 7, 31)]
    [InlineData(2027, 12, 31)]
    public void Stable_rehabilitation_identity_keeps_Friday_headcount_and_excludes_Saturday_across_month_and_year_boundaries(int year, int month, int day)
    {
        var friday = new DateOnly(year, month, day); var saturday = friday.AddDays(1);
        Assert.Equal(DayOfWeek.Friday, friday.DayOfWeek);
        var definition = SchedulingRules.RehabilitationMorning;
        foreach (var required in new[] { 0, 1, 3 })
        {
            Assert.True(SchedulingRules.Applicable(definition, "ORDINARY", friday));
            Assert.False(SchedulingRules.Applicable(definition, "ORDINARY", saturday));
            Assert.Equal(required, SchedulingRules.Required("ORDINARY", friday, required, definition));
            Assert.Equal(0, SchedulingRules.Required("ORDINARY", saturday, required, definition));
        }
        var time = ShiftClock.Boundaries(friday, ShiftType.DAY);
        var cell = new CandidateCell(definition, friday, time.StartsAt, time.EndsAt, 3, 1, ShiftType.DAY);
        var employees = Enumerable.Range(0, 3).Select(i => new Employee(Guid.NewGuid(), $"Employee {i}")).ToArray();
        var problem = new SchedulingProblem(year, month, "friday", [cell], employees, [], []);
        var result = new ScheduleOptimizer().Solve(problem, 5, CancellationToken.None);
        Assert.Equal(3, result.Cells.Single().Employees.Length);
        GeneratedDraftService.Validate(problem, result.Cells);
        var next = ShiftClock.Boundaries(saturday, ShiftType.DAY);
        var forged = cell with { Date = saturday, Start = next.StartsAt, End = next.EndsAt };
        Assert.False(problem.Eligible(employees[0], forged));
        Assert.Contains("WEEKEND_SHIFT_PROHIBITED", SchedulingRules.CandidateReasons(problem, forged, employees[0].Id));
    }
}
