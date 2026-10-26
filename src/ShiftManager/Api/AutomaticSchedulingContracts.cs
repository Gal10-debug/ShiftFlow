using System.ComponentModel.DataAnnotations;
namespace ShiftManager.Api;
public sealed record GenerateScheduleRequest([Range(2, 9998)] int Year, [Range(1, 12)] int Month);
public sealed record ScheduledEmployee(Guid UserId, string Name, bool Protected, bool IsShiftLead);
public sealed record GeneratedCell(Guid DefinitionId, DateOnly Date, DateTime StartsAt, DateTime EndsAt,
    int RequiredEmployees, ScheduledEmployee[] Employees)
{
    public int Shortage => Math.Max(0, RequiredEmployees - Employees.Length);
}
public sealed record StaffingSummary(int Required, int Filled, int Missing, int UnderstaffedCells, int FullyStaffedCells)
{
    public static StaffingSummary From(GeneratedCell[] cells) => new(cells.Sum(x => x.RequiredEmployees),
        cells.Sum(x => x.Employees.Length), cells.Sum(x => x.Shortage), cells.Count(x => x.Shortage > 0), cells.Count(x => x.Shortage == 0));
}
public sealed record GenerationResponse(Guid Id, int Year, int Month, string State, string? SolverStatus,
    int ProvenObjectives, bool CoverageOptimal, bool PublicationReady, string? ErrorCode,
    DateTime ExpiresAt, GeneratedCell[] Cells, StaffingSummary? Summary);
