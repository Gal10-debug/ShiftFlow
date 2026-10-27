using System.ComponentModel.DataAnnotations;
namespace ShiftManager.Api;
public sealed record SnapshotEmployee(Guid AssignmentId, Guid UserId, string Name, bool IsShiftLead);
public sealed record SnapshotCell(Guid DefinitionId, DateOnly Date, string Name, int DisplayOrder, string Period,
    string TimeRange, DateTime StartsAt, DateTime EndsAt, int RequiredEmployees, bool RequiresShiftLeadQualification, SnapshotEmployee[] Employees);
public sealed record LegacyWork(Guid AssignmentId, Guid UserId, string Name, DateOnly Date, string Period, DateTime StartsAt, DateTime EndsAt, bool IsShiftLead);
public sealed record ScheduleSnapshot(int Year, int Month, SnapshotCell[] Cells, LegacyWork[] LegacyAssignments);
public sealed record ReviewResponse(string State, long Revision, string Token, bool NeedsReview, bool PublicationReady,
    int Shortage, int OpenIssues, RosterProblem[] Conflicts, ScheduleSnapshot Draft);
public sealed record RosterProblem(Guid? AssignmentId, Guid? UserId, DateOnly Date, string Code);
public sealed record CandidateResponse(Guid UserId, string Name, bool Eligible, string[] Reasons);
public sealed record ReviewTokenRequest([Required, StringLength(64, MinimumLength = 64)] string ExpectedToken);
public sealed record ManualAssignmentRequest([Required, StringLength(64, MinimumLength = 64)] string ExpectedToken,
    string Operation, Guid? AssignmentId, Guid? UserId, Guid? DefinitionId, DateOnly? Date);
public sealed record PublishScheduleRequest([Required, StringLength(64, MinimumLength = 64)] string ExpectedToken,
    bool AcknowledgeShortages, bool AcknowledgeOpenIssues, [StringLength(2000)] string? Notes);
public sealed record PublicationResponse(Guid Id, int VersionNumber, string PublicationType, Guid PublishedByUserId,
    DateTime PublishedAt, string? Notes, ScheduleSnapshot? Snapshot, bool AcknowledgedShortages = false, bool AcknowledgedOpenIssues = false, int OpenIssueCount = 0);
public sealed record CreateIssueRequest(Guid PublicationId, Guid AssignmentId,
    [Required, StringLength(2000), RegularExpression(@"[\s\S]*\S[\s\S]*")] string Explanation);
public sealed record UpdateIssueRequest([Range(1, long.MaxValue)] long ExpectedVersion, string Status, [StringLength(2000)] string? Response);
