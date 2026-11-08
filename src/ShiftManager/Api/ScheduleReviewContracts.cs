using System.ComponentModel.DataAnnotations;
namespace ShiftManager.Api;
public sealed record SnapshotEmployee(Guid AssignmentId, Guid UserId, string Name, bool IsShiftLead, string? Note = null);
public sealed record SnapshotCell(Guid DefinitionId, DateOnly Date, string Name, int DisplayOrder, string Period,
    string TimeRange, DateTime StartsAt, DateTime EndsAt, int RequiredEmployees, bool RequiresShiftLeadQualification, SnapshotEmployee[] Employees, string Semantic = "ORDINARY", bool Applicable = true, string AssignmentMode = "Automatic");
public sealed record LegacyWork(Guid AssignmentId, Guid UserId, string Name, DateOnly Date, string Period, DateTime StartsAt, DateTime EndsAt, bool IsShiftLead, string? Note = null);
public sealed record ScheduleSnapshot(int Year, int Month, SnapshotCell[] Cells, LegacyWork[] LegacyAssignments);
public sealed record ReviewResponse(string State, long Revision, string Token, bool NeedsReview, bool PublicationReady,
    int Shortage, int OpenIssues, RosterProblem[] Conflicts, ScheduleSnapshot Draft, int EditingContractVersion = 1, bool FinalRevision = false, RosterProblem[]? PendingAbsenceWarnings = null);
public sealed record RosterProblem(Guid? AssignmentId, Guid? UserId, DateOnly Date, string Code);
public sealed record CandidateResponse(Guid UserId, string Name, bool Eligible, string[] Reasons, bool IsShiftLeadQualified = false, bool Preferred = false);
public sealed record ReviewTokenRequest([Required, StringLength(64, MinimumLength = 64)] string ExpectedToken);
public sealed record ManualAssignmentRequest([Required, StringLength(64, MinimumLength = 64)] string ExpectedToken,
    string Operation, Guid? AssignmentId, Guid? UserId, Guid? DefinitionId, DateOnly? Date, [StringLength(1000)] string? Note = null);
public sealed record PublishScheduleRequest([Required, StringLength(64, MinimumLength = 64)] string ExpectedToken,
    bool AcknowledgeShortages, bool AcknowledgeOpenIssues, [StringLength(2000)] string? Notes);
public sealed record PublicationResponse(Guid Id, int VersionNumber, string PublicationType, Guid PublishedByUserId,
    DateTime PublishedAt, string? Notes, ScheduleSnapshot? Snapshot, bool AcknowledgedShortages = false, bool AcknowledgedOpenIssues = false, int OpenIssueCount = 0, string? PublishedByName = null, bool Hidden = false, bool Deleted = false, long ControlVersion = 0, PersonalScheduleResponse? PersonalSchedule = null);
public sealed record CreateIssueRequest(Guid PublicationId, Guid AssignmentId,
    [Required, StringLength(2000), RegularExpression(@"[\s\S]*\S[\s\S]*")] string Explanation);
public sealed record UpdateIssueRequest([Range(1, long.MaxValue)] long ExpectedVersion, string Status, [StringLength(2000)] string? Response);

public sealed record OnCallPairRequest([Required, StringLength(64, MinimumLength = 64)] string ExpectedToken,
    string Operation, DateOnly Friday, Guid? UserId, Guid? AssignmentId, bool AcknowledgeAdjacentPeriod, [StringLength(1000)] string? Note = null);

public sealed record PublicationControlRequest(long ExpectedVersion, string Operation,
    [Required, StringLength(2000), RegularExpression(@"[\s\S]*\S[\s\S]*")] string Reason);
public sealed record ExchangeCreateRequest(Guid PublicationId, Guid AssignmentId, Guid ReplacementUserId);
public sealed record ExchangeDecisionRequest(long ExpectedVersion, string Decision, [StringLength(2000)] string? Reason = null);
public sealed record ExchangeAssignmentResponse(Guid AssignmentId, Guid PublicationId, Guid DefinitionId, DateOnly Date,
    DateTime StartsAt, DateTime EndsAt, bool IsShiftLead, string Name);
public sealed record ExchangeResponse(Guid Id, Guid RequesterUserId, string RequesterName, Guid ReplacementUserId,
    string ReplacementName, string Status, long Version, string? Reason, DateTime CreatedAt,
    ExchangeAssignmentResponse[] Assignments);

public sealed record ExchangePeriodToken(int Year, int Month, string ExpectedToken);
public sealed record ExchangePublicationRequest(ExchangePeriodToken[] Periods, bool AcknowledgeShortages,
    bool AcknowledgeOpenIssues, [StringLength(2000)] string? Notes);
