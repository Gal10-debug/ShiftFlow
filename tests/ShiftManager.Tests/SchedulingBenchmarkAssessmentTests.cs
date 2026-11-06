using ShiftManager.Api;
using ShiftManager.Services.AutomaticScheduling;

namespace ShiftManager.Tests;

public sealed class SchedulingBenchmarkAssessmentTests
{
    [Theory]
    [InlineData(false, false, "UNPROVEN_TARGET_NOT_MET", false)]
    [InlineData(false, true, "UNPROVEN_TARGET_MET", false)]
    [InlineData(true, false, "PROVEN_TARGET_VIOLATION", true)]
    [InlineData(true, true, "PROVEN_TARGET_MET", false)]
    public void Fairness_assessment_preserves_missed_targets_and_distinguishes_proof_from_incumbents(
        bool proven, bool balanced, string outcome, bool regression)
    {
        var stages = new[] {
            new OptimizationStageResult("CRITICAL_COVERAGE", "OPTIMAL", 0, 0, 0, true),
            new OptimizationStageResult("HIGH_COVERAGE", "OPTIMAL", 0, 0, 0, true),
            new OptimizationStageResult("NORMAL_COVERAGE", "OPTIMAL", 15, 15, 0, true),
            new OptimizationStageResult("FAIRNESS", proven ? "OPTIMAL" : "FEASIBLE", -1000, proven ? -1000 : -500, 30, proven),
            new OptimizationStageResult("PREFERENCES", "NOT_RUN", null, null, 0, false),
            new OptimizationStageResult("STABILITY", "NOT_RUN", null, null, 0, false) };
        var result = new OptimizationResult("FEASIBLE", proven ? 4 : 3, [], stages);
        // A synthetic 0/15 cohort reproduces the reported spread of 15; the CI
        // trace did not supply its actual minimum/maximum. An unproven miss
        // must remain visible rather than being labeled an acceptable target.
        var counts = balanced ? new[] { 7, 8 } : new[] { 0, 15 };
        var metrics = counts.Select((count, i) => new SchedulingStressTests.EmployeeMetrics(
            SchedulingValidationScenarios.Employee(i + 1).Id, false, 20, count, count * 12, 0, 0, 0)).ToArray();
        var assessment = BenchmarkFairnessAssessment.Evaluate(result, metrics);
        Assert.Equal(proven, assessment.ProvenOptimal);
        Assert.Equal(balanced, assessment.TargetMet);
        Assert.Equal(outcome, assessment.Outcome);
        Assert.Equal(regression, assessment.ProvenRegression);
        var cohort = Assert.Single(assessment.Cohorts);
        Assert.Equal(balanced ? 1 : 15, cohort.Maximum - cohort.Minimum);
        Assert.Equal(balanced ? 0 : 1, cohort.IdleEmployees);
    }
}
