using ShiftManager.Api;
using ShiftManager.Domain;
using ShiftManager.Services.AutomaticScheduling;
namespace ShiftManager.Tests;

public sealed class WeekendBoardRulesTests
{
    private static Guid Definition(int slot) => Guid.Parse($"40000000-0000-0000-0000-{slot:000000000000}");
    public static IEnumerable<object[]> Definitions() => Enumerable.Range(1, 51).Select(i => new object[] { i });

    [Theory, MemberData(nameof(Definitions))]
    public void All_51_stable_definitions_have_correct_weekend_demand_and_eligibility(int slot)
    {
        var semantic = slot == 16 ? SchedulingRules.OnCall : slot == 1 ? SchedulingRules.SixNine : "ORDINARY";
        var id = Definition(slot);
        foreach (var date in new[] { new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 3) })
        {
            var allowed = slot is 2 or 3 or 4 or 5 or 16;
            Assert.Equal(allowed, SchedulingRules.Applicable(id, semantic, date));
            Assert.Equal(slot == 3 ? 1 : allowed ? 3 : 0, SchedulingRules.Required(semantic, date, 3, id));
        }
        Assert.Equal(slot != 16, SchedulingRules.Applicable(id, semantic, new(2026, 10, 4)));
    }

    [Fact]
    public void Solver_does_not_allocate_or_report_shortages_for_prohibited_weekend_rows()
    {
        var user = new Employee(Guid.NewGuid(), "עובד", true);
        var cells = Enumerable.Range(1, 51).SelectMany(slot => new[] { 2, 3 }.Select(day => {
            var date = new DateOnly(2026, 10, day); var time = ShiftClock.Boundaries(date, ShiftType.DAY);
            var semantic = slot == 16 ? SchedulingRules.OnCall : "ORDINARY";
            return new CandidateCell(Definition(slot), date, time.StartsAt, time.EndsAt,
                SchedulingRules.Required(semantic, date, 1, Definition(slot)), 1, ShiftType.DAY, slot == 2, semantic);
        })).ToArray();
        var problem = new SchedulingProblem(2026, 10, "test", cells, [user], [], []);
        var result = new ScheduleOptimizer().Solve(problem, 5, CancellationToken.None);
        GeneratedDraftService.Validate(problem, result.Cells);
        Assert.All(result.Cells.Where(c => !new[] { 2, 3, 4, 5, 16 }.Select(Definition).Contains(c.DefinitionId)), c => {
            Assert.Empty(c.Employees); Assert.Equal(0, c.RequiredEmployees); Assert.Equal(0, c.Shortage);
        });
    }

    [Fact]
    public void Forged_manual_candidate_and_persisted_weekend_work_are_rejected_even_without_pair_validation()
    {
        var date = new DateOnly(2026, 10, 2); var time = ShiftClock.Boundaries(date, ShiftType.DAY);
        var cell = new CandidateCell(Definition(6), date, time.StartsAt, time.EndsAt, 1, 1, ShiftType.DAY);
        var user = new Employee(Guid.NewGuid(), "עובד");
        var problem = new SchedulingProblem(2026, 10, "test", [cell], [user], [], []);
        Assert.False(problem.Eligible(user, cell));
        Assert.Contains("WEEKEND_SHIFT_PROHIBITED", SchedulingRules.CandidateReasons(problem, cell, user.Id));
        var work = SchedulingRules.Proposed(user.Id, cell);
        Assert.Contains(SchedulingRules.Violations([work], 2026, 10, pairing: false), v => v.Code == "WEEKEND_SHIFT_PROHIBITED");
        var error = Assert.Throws<ApiException>(() => new ScheduleOptimizer().Solve(problem with { Work = [work] }, 5, CancellationToken.None));
        Assert.Equal("WEEKEND_SHIFT_PROHIBITED", error.Code);
    }
}
