using System.ComponentModel.DataAnnotations;
namespace ShiftManager.Api;

public sealed record AbsencePattern(string Reason, DateOnly StartDate, DateOnly? EndDate, bool Recurring,
    int[] Weekdays, bool FullDay, TimeOnly? StartTime, TimeOnly? EndTime, [StringLength(2000)] string? Explanation);
public sealed record AbsenceChange([Range(1, long.MaxValue)] long ExpectedVersion, string Kind, AbsencePattern? Pattern);
public sealed record AbsenceDecisionRequest([Range(1, long.MaxValue)] long ExpectedVersion, string Decision,
    [StringLength(2000)] string? RejectionReason, string? ConflictToken = null, bool ConfirmDraftRemovals = false);
public sealed record AbsenceVersion([Range(1, long.MaxValue)] long ExpectedVersion);
public sealed record AbsenceDecisionResponse(Guid ActorUserId, string ActorName, string Decision, DateTime OccurredAt, string? RejectionReason);
public sealed record AbsenceConflictItem(Guid AssignmentId, Guid SchedulePeriodId, int Year, int Month, DateOnly Date,
    DateTime StartsAt, DateTime EndsAt, string ShiftName, string State, Guid? PublicationId, int? PublicationVersion,
    bool Removable, string? RemovalBlock = null);
public sealed record AbsenceConflictReport(string Token, AbsenceConflictItem[] Items);
public sealed record AbsenceResponse(Guid Id, Guid UserId, string EmployeeName, Guid? TeamId, string? TeamName,
    string Kind, string Status, AbsencePattern Pattern, Guid? ReplacesId, long Version,
    DateTime CreatedAt, DateTime UpdatedAt, AbsenceDecisionResponse? Decision, AbsenceConflictItem[] UnresolvedConflicts);
