using System.ComponentModel.DataAnnotations;
namespace ShiftManager.Api;

public sealed record WindowRequest([Required] DateTimeOffset? OpensAt, [Required] DateTimeOffset? ClosesAt);
public sealed record WindowStateRequest([Required] bool? IsOpen);
public sealed record SelectionRequest(DateOnly Date, [Required] string ShiftType, [Required] string State);
public sealed record SubmitAvailabilityRequest([Range(0, long.MaxValue)] long ExpectedVersion,
    [Required, MaxLength(62)] SelectionRequest[] Selections);
public sealed record WindowResponse(string Status, string AdministrativeState, DateTime? OpensAt, DateTime? ClosesAt);
public sealed record PeriodResponse(Guid Id, int Year, int Month, WindowResponse Window, bool Submitted, DateTime? LastSubmittedAt);
public sealed record SelectionResponse(DateOnly Date, string ShiftType, string State);
public sealed record WeekResponse(DateOnly StartsOn, int OtherMonthBlocks);
public sealed record AvailabilityResponse(int Year, int Month, Guid UserId, WindowResponse Window,
    bool Submitted, DateTime? LastSubmittedAt, long Version, SelectionResponse[] Selections, WeekResponse[] Weeks);
public sealed record EmployeeSubmissionResponse(Guid UserId, string FirstName, string LastName, bool Submitted, DateTime? LastSubmittedAt);
public sealed record WindowSummaryResponse(int Year, int Month, WindowResponse Window, int EligibleCount, int SubmittedCount,
    EmployeeSubmissionResponse[] Employees);
