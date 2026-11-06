using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ShiftManager.Api;
using ShiftManager.Domain;
using ShiftManager.Services.AutomaticScheduling;
using Xunit.Abstractions;

namespace ShiftManager.Tests;

public sealed class SchedulingStressTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Scenarios() => new[] {
        ("normal", 2026, 11), ("normal-repeat", 2026, 11),
        ("protected", 2026, 11), ("protected-repeat", 2026, 11),
        ("moderate-absences", 2026, 11), ("severe-shortage", 2026, 11),
        ("insufficient-leads", 2026, 11), ("widespread-absences", 2026, 11), ("studies", 2026, 11),
        ("reserve-duty", 2026, 11), ("overlapping-restrictions", 2026, 11), ("heavily-constrained", 2026, 11),
        ("variable-headcount", 2026, 11), ("year-boundary", 2027, 1), ("spring-dst", 2026, 3), ("autumn-dst", 2026, 10)
    }.Select(s => new object[] { s.Item1, s.Item2, s.Item3 });

    [Theory, MemberData(nameof(Scenarios))]
    public void Monthly_schedules_pass_independent_validation_and_emit_metrics(string scenario, int year, int month)
    {
        var p = SchedulingValidationScenarios.Month(scenario, year, month);
        if (scenario == "year-boundary")
        {
            var boundary = SchedulingValidationScenarios.Cell(5, new(2026, 12, 31), ShiftTimeRange.NIGHT);
            p = p with { Work = [SchedulingValidationScenarios.Work(boundary, p.Employees[0])] };
        }
        if (scenario is "protected" or "protected-repeat")
        {
            var cell = p.Cells.First(c => c.DefinitionId == SchedulingValidationScenarios.Id(2));
            p = p with { Work = [SchedulingValidationScenarios.Work(cell, p.Employees.First(e => e.IsShiftLeadQualified), lead: true)] };
        }
        var seconds = double.Parse(Environment.GetEnvironmentVariable("SCHEDULING_STRESS_SECONDS") ?? "8", System.Globalization.CultureInfo.InvariantCulture);
        var watch = Stopwatch.StartNew();
        var result = new ScheduleOptimizer().Solve(p, seconds, default);
        watch.Stop();
        Assert.True(result.Status is "OPTIMAL" or "FEASIBLE", $"{scenario}: {result.Status}, {watch.Elapsed}");
        Assert.Equal(result.Status == "OPTIMAL", result.ProvenObjectives == 7);
        var violations = IndependentScheduleValidator.Check(p, result.Cells);
        var metrics = Metrics(p, result);
        var report = new { Scenario = scenario, Year = year, Month = month, SchedulingValidationScenarios.Seed,
            Employees = p.Employees.Length, Cells = p.Cells.Length, Required = p.Cells.Sum(c => c.Required),
            ApprovedAbsences = p.ApprovedAbsences!.Length, Blocks = p.Preferences.Length, Protected = p.Work.Count(w => !w.Replaceable),
            BudgetSeconds = seconds, ElapsedSeconds = watch.Elapsed.TotalSeconds, result.Status, result.ProvenObjectives,
            Termination = result.Status == "OPTIMAL" ? "All seven objectives proven" : "Shared budget: incumbent; later objectives unproven",
            Filled = result.Cells.Sum(c => c.Employees.Length), Missing = result.Cells.Sum(c => c.Shortage),
            Violations = violations, AssignmentHash = Hash(result), EmployeesMetrics = metrics };
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        output.WriteLine(json);
        var directory = Environment.GetEnvironmentVariable("SCHEDULING_VALIDATION_REPORT_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, $"{scenario}.json"), json + Environment.NewLine);
        }
        Assert.Empty(violations);
        GeneratedDraftService.Validate(p, result.Cells);
        if (scenario == "widespread-absences")
            Assert.All(metrics.Where(e => e.Opportunities == 0), e => Assert.Equal(0, e.Total));
        Assert.All(metrics, e => Assert.InRange(e.WeekendAllocations, 0, 2));
    }

    internal sealed record EmployeeMetrics(Guid Id, bool Qualified, int Opportunities, int Total, int Nights, int WeekendAllocations, int HighPriority);
    internal static EmployeeMetrics[] Metrics(SchedulingProblem p, OptimizationResult result) => p.Employees.Select(e => {
        var work = result.Cells.SelectMany(c => c.Employees.Where(m => m.UserId == e.Id).Select(_ => {
            var input = p.Cells.Single(x => x.DefinitionId == c.DefinitionId && x.Date == c.Date);
            return SchedulingValidationScenarios.Work(input, e, Array.IndexOf(p.Cells, input) + 1);
        })).ToArray();
        var opportunities = p.Cells.Count(c => c.Required > 0 && (!c.RequiresShiftLeadQualification || e.IsShiftLeadQualified)
            && !(p.ApprovedAbsences ?? []).Any(a => a.UserId == e.Id && IndependentScheduleValidator.Absent(a, c.Start, c.End))
            && !p.Preferences.Any(b => b.UserId == e.Id && b.State == PreferenceType.BLOCKED &&
                IndependentScheduleValidator.Utc(b.Date, b.Type == ShiftType.DAY ? 7 : 19) < c.End &&
                IndependentScheduleValidator.Utc(b.Type == ShiftType.DAY ? b.Date : b.Date.AddDays(1), b.Type == ShiftType.DAY ? 19 : 7) > c.Start));
        return new EmployeeMetrics(e.Id, e.IsShiftLeadQualified, opportunities, work.Length, work.Count(w => w.AnchorType == ShiftType.NIGHT),
            IndependentScheduleValidator.WeekendAllocations(work), work.Count(w => Enumerable.Range(1, 7).Any(i => w.DefinitionId == SchedulingValidationScenarios.Id(i))));
    }).ToArray();

    internal static string Hash(OptimizationResult result) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n",
        result.Cells.OrderBy(c => c.Date).ThenBy(c => c.DefinitionId).Select(c => $"{c.Date:yyyy-MM-dd}/{c.DefinitionId}/{string.Join(',', c.Employees.Select(e => e.UserId).Order())}")))));

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Controlled_single_worker_runs_repeat_exactly_with_and_without_protected_work(bool protect)
    {
        var p = SchedulingValidationScenarios.Month("normal", 2026, 11);
        p = p with { Cells = p.Cells.Where(c => c.Date.Day <= 4).ToArray(), StaffingCells = null, Employees = p.Employees.Take(8).ToArray() };
        if (protect)
        {
            var cell = p.Cells.First(c => c.DefinitionId == SchedulingValidationScenarios.Id(2));
            p = p with { Work = [SchedulingValidationScenarios.Work(cell, p.Employees.First(e => e.IsShiftLeadQualified), lead: true)] };
        }
        var results = Enumerable.Range(0, 3).Select(_ => new ScheduleOptimizer().Solve(p, 10, default)).ToArray();
        Assert.All(results, r => { Assert.Equal("OPTIMAL", r.Status); Assert.Equal(7, r.ProvenObjectives); Assert.Empty(IndependentScheduleValidator.Check(p, r.Cells)); });
        Assert.Single(results.Select(Hash).Distinct());
        output.WriteLine($"Three identical optimal solves, protected={protect}, assignment SHA256={Hash(results[0])}");
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Comparable_employees_have_balanced_proven_total_loads(bool restrictions)
    {
        var first = new DateOnly(2026, 11, 1);
        var cells = Enumerable.Range(0, 30).Where(i => !IndependentScheduleValidator.Weekend(first.AddDays(i)))
            .Select(i => SchedulingValidationScenarios.Cell(26, first.AddDays(i))).ToArray();
        var employees = Enumerable.Range(1, 4).Select(i => SchedulingValidationScenarios.Employee(i, false)).ToArray();
        var prefs = restrictions ? cells.Where(c => c.Date.DayOfWeek == DayOfWeek.Monday)
            .Select(c => new Preference(employees[0].Id, c.Date, ShiftType.DAY, PreferenceType.BLOCKED)).ToArray() : [];
        var p = SchedulingValidationScenarios.Problem(cells, employees, preferences: prefs);
        var result = new ScheduleOptimizer().Solve(p, 10, default);
        Assert.Equal("OPTIMAL", result.Status);
        Assert.Empty(IndependentScheduleValidator.Check(p, result.Cells));
        Assert.Equal(cells.Length, result.Cells.Sum(c => c.Employees.Length));
        var metrics = Metrics(p, result);
        var comparable = restrictions ? metrics.Skip(1).ToArray() : metrics;
        Assert.InRange(comparable.Max(m => m.Total) - comparable.Min(m => m.Total), 0, 1);
        if (restrictions) Assert.True(metrics[0].Total <= comparable.Max(m => m.Total));
        output.WriteLine(JsonSerializer.Serialize(metrics));
    }
}
