using ShiftManager.Domain;
using ShiftManager.Services.AutomaticScheduling;
namespace ShiftManager.Tests;

public sealed class PrioritySchedulingTests
{
    private static CandidateCell Cell(int slot, DateOnly? date = null, bool expert = false, AssignmentMode mode = AssignmentMode.Automatic)
    {
        var d = date ?? new DateOnly(2026, 11, 1);
        var t = ShiftClock.Boundaries(d, ShiftType.DAY);
        return new(Guid.Parse($"40000000-0000-0000-0000-{slot:000000000000}"), d, t.StartsAt, t.EndsAt, 1, 1, ShiftType.DAY, expert, AssignmentMode: mode, SchedulingPriority: slot <= 7 ? SchedulingPriority.Critical : SchedulingPriority.Normal);
    }
    private static OptimizationResult Solve(CandidateCell[] cells, Employee[] employees, Work[]? work = null) =>
        new ScheduleOptimizer().Solve(new(2026, 11, "", cells, employees, work ?? [], []), 10, default);
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void Limited_employee_prefers_each_priority_row_to_lower_rows(int slot)
    {
        var result = Solve([Cell(8), Cell(slot)], [new(Guid.NewGuid(), "עובד")]);
        Assert.Empty(result.Cells[0].Employees);
        Assert.Single(result.Cells[1].Employees);
        Assert.Equal("OPTIMAL", result.Status);
    }
    [Fact]
    public void Configured_priority_precedes_qualification_row_importance_without_relaxing_eligibility()
    {
        var employees = new[] { new Employee(Guid.NewGuid(), "מומחה", true) };
        var r = Solve([Cell(2), Cell(8, expert: true)], employees);
        Assert.Single(r.Cells[0].Employees); Assert.Empty(r.Cells[1].Employees);
        r = Solve([Cell(8, expert: true), Cell(2, expert: true)], employees);
        Assert.Single(r.Cells[1].Employees);
    }
    [Fact]
    public void Priority_coverage_cannot_be_outweighed_by_multiple_lower_row_assignments()
    {
        var r = Solve([Cell(2) with { End = Cell(2).End.AddDays(2) }, Cell(8), Cell(9, new(2026, 11, 2)), Cell(10, new(2026, 11, 3))], [new(Guid.NewGuid(), "עובד")]);
        Assert.Single(r.Cells[0].Employees);
        Assert.All(r.Cells.Skip(1), c => Assert.Empty(c.Employees));
    }
    [Fact]
    public void Manual_only_has_no_automatic_variables_and_preserves_fixed_members()
    {
        var c = Cell(8, mode: AssignmentMode.ManualOnly);
        var e = new Employee(Guid.NewGuid(), "עובד");
        Assert.Empty(Solve([c], [e]).Cells[0].Employees);
        var w = new Work(Guid.NewGuid(), e.Id, c.DefinitionId, c.Date, c.Start, c.End, Guid.NewGuid(), false, false, ShiftType.DAY);
        Assert.True(Solve([c], [e], [w]).Cells[0].Employees.Single().Protected);
    }
}
