using ShiftManager.Api;
using ShiftManager.Domain;
using ShiftManager.Services;
using ShiftManager.Services.AutomaticScheduling;
using static ShiftManager.Tests.SchedulingValidationScenarios;

namespace ShiftManager.Tests;

public sealed class PriorityOptimizationTests
{
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)] [InlineData(12)]
    public void Priority_coverage_matches_independent_exhaustive_optimum_under_shortages(int seed)
    {
        var date = new DateOnly(2026, 11, 1); var random = new Random(seed);
        var employees = new[] { Employee(1), Employee(2, false) };
        var cells = Enumerable.Range(0, 5).Select(i => Cell(26 - i, date.AddDays(i / 2),
            i % 2 == 0 ? ShiftTimeRange.DAY : ShiftTimeRange.NIGHT, qualified: i % 3 == 0)
            with { SchedulingPriority = (SchedulingPriority)random.Next(3) }).ToArray();
        var work = seed % 3 == 0 ? new[] { Work(cells[0], employees[0]) } : [];
        var absences = seed % 2 == 0 ? new[] { new AbsenceRule(Id(100), employees[1].Id, date, date, false, [], true, null, null) } : [];
        var p = Problem(cells, employees, work, absences: absences);
        var best = new[] { -1, -1, -1 };
        // Every cell may be empty or have either employee. 3^5=243 independent
        // rosters: qualification, absence, protected work and consecutive shifts
        // are checked by the independent oracle, never the solver's constraints.
        for (var mask = 0; mask < 243; mask++)
        {
            var value = mask;
            var output = cells.Select(c => {
                var choice = value % 3; value /= 3;
                var original = work.SingleOrDefault(w => w.DefinitionId == c.DefinitionId && w.Date == c.Date);
                var members = choice == 0 ? [] : new[] { new ScheduledEmployee(employees[choice - 1].Id, "employee",
                    original?.UserId == employees[choice - 1].Id, false, original?.AssignmentId, original?.Note) };
                return new GeneratedCell(c.DefinitionId, c.Date, c.Start, c.End, c.Required, members);
            }).ToArray();
            if (IndependentScheduleValidator.Check(p, output).Length != 0) continue;
            var vector = Coverage(p, output);
            if (Compare(vector, best) > 0) best = vector;
        }
        Assert.True(best[0] >= 0);
        var result = new ScheduleOptimizer().Solve(p, 5, default);
        Assert.Equal("OPTIMAL", result.Status);
        Assert.Empty(IndependentScheduleValidator.Check(p, result.Cells));
        Assert.Equal(best, Coverage(p, result.Cells));
        CheckDiagnostics(result);
    }

    internal static int[] Coverage(SchedulingProblem p, GeneratedCell[] output) =>
        new[] { SchedulingPriority.Critical, SchedulingPriority.High, SchedulingPriority.Normal }
            .Select(priority => output.Where(o => p.Cells.Any(c => c.DefinitionId == o.DefinitionId && c.Date == o.Date && c.SchedulingPriority == priority))
                .Sum(o => o.Employees.Length)).ToArray();
    private static int Compare(int[] left, int[] right)
    {
        for (var i = 0; i < left.Length; i++) if (left[i] != right[i]) return left[i].CompareTo(right[i]);
        return 0;
    }
    internal static void CheckDiagnostics(OptimizationResult result)
    {
        Assert.NotNull(result.Stages); Assert.Equal(ScheduleOptimizer.ObjectiveCount, result.Stages.Length);
        Assert.Equal(result.ProvenObjectives, result.Stages.TakeWhile(s => s.ProvenOptimal).Count());
        foreach (var stage in result.Stages.Where(s => s.Status == "FEASIBLE"))
        {
            Assert.NotNull(stage.Value); Assert.NotNull(stage.Bound);
            Assert.True(stage.Bound >= stage.Value);
        }
        foreach (var stage in result.Stages.Where(s => s.ProvenOptimal))
        {
            Assert.Equal("OPTIMAL", stage.Status); Assert.NotNull(stage.Value); Assert.NotNull(stage.Bound);
            Assert.Equal(stage.Value!.Value, stage.Bound!.Value, 6);
        }
        var unproven = result.Stages.SkipWhile(s => s.ProvenOptimal).ToArray();
        Assert.All(unproven.Skip(1), s => Assert.Equal("NOT_RUN", s.Status));
        Assert.Equal(result.ProvenObjectives == ScheduleOptimizer.ObjectiveCount, result.Status == "OPTIMAL");
    }

    [Theory]
    [InlineData(SchedulingPriority.Critical, SchedulingPriority.High)]
    [InlineData(SchedulingPriority.High, SchedulingPriority.Normal)]
    public void Configured_priority_wins_over_lower_coverage_preferences_and_stability(SchedulingPriority high, SchedulingPriority low)
    {
        var date = new DateOnly(2026, 11, 1); var e = Employee(1);
        var important = Cell(26, date, ShiftTimeRange.NIGHT) with { SchedulingPriority = high };
        var others = new[] { Cell(2, date), Cell(3, date.AddDays(1)) }.Select(c => c with { SchedulingPriority = low }).ToArray();
        var p = Problem([important, .. others], [e], others.Select((c, i) => Work(c, e, i + 1, replaceable: true)).ToArray(),
            others.Select(c => new Preference(e.Id, c.Date, ShiftType.DAY, PreferenceType.PREFERRED)).ToArray());
        var result = new ScheduleOptimizer().Solve(p, 5, default);
        Assert.Single(result.Cells[0].Employees); Assert.All(result.Cells.Skip(1), c => Assert.Empty(c.Employees));
        Assert.Empty(IndependentScheduleValidator.Check(p, result.Cells)); CheckDiagnostics(result);
    }

    [Fact]
    public void Equal_priority_rows_have_equal_coverage_weight_regardless_of_UUID_or_qualification()
    {
        var date = new DateOnly(2026, 11, 1);
        var longQualified = Cell(2, date, ShiftTimeRange.NIGHT, qualified: true);
        var p = Problem([longQualified, Cell(26, date), Cell(25, date.AddDays(1))]);
        var result = new ScheduleOptimizer().Solve(p, 5, default);
        Assert.Empty(result.Cells[0].Employees); Assert.Equal(2, result.Cells.Sum(c => c.Employees.Length));
        Assert.Empty(IndependentScheduleValidator.Check(p, result.Cells));
    }

    [Fact]
    public void Timeout_reports_unproven_stages_without_claiming_a_solution()
    {
        var p = Problem([Cell(26, new(2026, 11, 1))]);
        var result = new ScheduleOptimizer().Solve(p, 0, default);
        Assert.Equal("TIMEOUT", result.Status); Assert.Empty(result.Cells); CheckDiagnostics(result);
        Assert.All(result.Stages!, s => { Assert.Equal("NOT_RUN", s.Status); Assert.Null(s.Value); Assert.Null(s.Bound); });
    }
}
