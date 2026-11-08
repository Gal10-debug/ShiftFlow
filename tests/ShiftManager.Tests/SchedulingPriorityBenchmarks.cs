using System.Diagnostics;
using System.Text.Json;
using ShiftManager.Services.AutomaticScheduling;
using Xunit.Abstractions;

namespace ShiftManager.Tests;

// One test executes all six experiments sequentially; the normal production
// timeout remains 30 seconds. Exclude Category=SchedulingBenchmark for quick runs.
[Collection(SchedulingBenchmarkCollection.Name)]
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
            var metrics = SchedulingStressTests.Metrics(p, result);
            var fairness = scenario == "normal" ? BenchmarkFairnessAssessment.Evaluate(result, metrics) : null;
            var report = new { Scenario = scenario, BudgetSeconds = seconds, ElapsedSeconds = watch.Elapsed.TotalSeconds, result.ModelBuildSeconds,
                ProcessPeakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64,
                result.Status, result.ProvenObjectives, CoverageCriticalHighNormal = PriorityOptimizationTests.Coverage(p, result.Cells),
                Missing = p.Cells.Sum(c => c.Required) - result.Cells.Sum(c => c.Employees.Length),
                result.Stages, Fairness = fairness, EmployeesMetrics = metrics };
            reports.Add(report);
            // Save each trial before assertions so a failure cannot discard its
            // incumbent, bounds, proof flags or missed quality targets.
            output.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            var directory = Environment.GetEnvironmentVariable("SCHEDULING_VALIDATION_REPORT_DIRECTORY");
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "priority-benchmarks.json"),
                    JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
            }
            Assert.True(result.Status is "OPTIMAL" or "FEASIBLE");
            Assert.Empty(IndependentScheduleValidator.Check(p, result.Cells)); GeneratedDraftService.Validate(p, result.Cells);
            PriorityOptimizationTests.CheckDiagnostics(result);
            Assert.False(fairness?.ProvenRegression ?? false,
                $"Proven fairness violates the normal fixture's quality target: {JsonSerializer.Serialize(fairness)}");
        }
    }
}

// Benchmarks must not compete with other test collections for their CPU budget.
// Other tests retain xUnit's normal parallelization.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SchedulingBenchmarkCollection
{
    public const string Name = "Scheduling benchmarks";
}

internal sealed record BenchmarkCohort(bool Qualified, int Employees, int Minimum, int Maximum, int IdleEmployees);
internal sealed record BenchmarkFairnessAssessment(bool ProvenOptimal, bool TargetMet, BenchmarkCohort[] Cohorts)
{
    public bool ProvenRegression => ProvenOptimal && !TargetMet;
    public string Outcome => (ProvenOptimal, TargetMet) switch {
        (true, true) => "PROVEN_TARGET_MET", (true, false) => "PROVEN_TARGET_VIOLATION",
        (false, true) => "UNPROVEN_TARGET_MET", _ => "UNPROVEN_TARGET_NOT_MET" };

    internal static BenchmarkFairnessAssessment Evaluate(OptimizationResult result, SchedulingStressTests.EmployeeMetrics[] metrics)
    {
        var cohorts = metrics.GroupBy(e => e.Qualified).Select(g => new BenchmarkCohort(g.Key, g.Count(),
            g.Min(e => e.Total), g.Max(e => e.Total), g.Count(e => e.Total == 0))).ToArray();
        return new(result.Stages?.SingleOrDefault(s => s.Objective == "FAIRNESS")?.ProvenOptimal == true,
            cohorts.Length > 0 && cohorts.All(c => c.Maximum - c.Minimum <= 2 && c.IdleEmployees == 0), cohorts);
    }
}
