using System.Diagnostics;
using System.Text.Json;
using ShiftManager.Services.AutomaticScheduling;
using Xunit.Abstractions;

namespace ShiftManager.Tests;

// One test executes all six experiments sequentially; the normal production
// timeout remains 30 seconds. Exclude Category=SchedulingBenchmark for quick runs.
public sealed class SchedulingPriorityBenchmarks(ITestOutputHelper output)
{
    [Fact, Trait("Category", "SchedulingBenchmark")]
    public void Compare_normal_and_severe_shortage_at_30_60_and_120_seconds()
    {
        var reports = new List<object>();
        var scenarios = (Environment.GetEnvironmentVariable("SCHEDULING_BENCHMARK_SCENARIOS") ?? "normal,severe-shortage").Split(',');
        var budgets = (Environment.GetEnvironmentVariable("SCHEDULING_BENCHMARK_BUDGETS") ?? "30,60,120").Split(',').Select(int.Parse).ToArray();
        Assert.All(scenarios, s => Assert.Contains(s, new[] { "normal", "severe-shortage" }));
        Assert.All(budgets, s => Assert.Contains(s, new[] { 30, 60, 120 }));
        foreach (var scenario in scenarios)
        foreach (var seconds in budgets)
        {
            var p = SchedulingValidationScenarios.Month(scenario, 2026, 11);
            var watch = Stopwatch.StartNew(); var result = new ScheduleOptimizer().Solve(p, seconds, default); watch.Stop();
            Assert.True(result.Status is "OPTIMAL" or "FEASIBLE");
            Assert.Empty(IndependentScheduleValidator.Check(p, result.Cells)); GeneratedDraftService.Validate(p, result.Cells);
            PriorityOptimizationTests.CheckDiagnostics(result);
            var metrics = SchedulingStressTests.Metrics(p, result);
            if (scenario == "normal")
                foreach (var comparable in metrics.GroupBy(e => e.Qualified))
                {
                    // Regression for the original 0–27 imbalance on this fixed,
                    // unrestricted monthly fixture, not a new production hard cap.
                    Assert.InRange(comparable.Max(e => e.Total) - comparable.Min(e => e.Total), 0, 2);
                    Assert.All(comparable, e => Assert.True(e.Total > 0));
                }
            reports.Add(new { Scenario = scenario, BudgetSeconds = seconds, ElapsedSeconds = watch.Elapsed.TotalSeconds,
                result.Status, result.ProvenObjectives, CoverageCriticalHighNormal = PriorityOptimizationTests.Coverage(p, result.Cells),
                Missing = result.Cells.Sum(c => c.Shortage), result.Stages, EmployeesMetrics = metrics });
        }
        var json = JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }); output.WriteLine(json);
        var directory = Environment.GetEnvironmentVariable("SCHEDULING_VALIDATION_REPORT_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(directory)) { Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "priority-benchmarks.json"), json + Environment.NewLine); }
    }
}
