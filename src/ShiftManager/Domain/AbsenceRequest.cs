namespace ShiftManager.Domain;

// A request is an immutable proposal. Approved replacements cancel their predecessor
// only in the same decision transaction; a pending change never weakens a restriction.
public sealed class AbsenceRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public string Kind { get; set; } = "CREATE";
    public string Status { get; set; } = "PENDING";
    public string Reason { get; set; } = "STUDIES";
    public string? Explanation { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool Recurring { get; set; }
    public bool FullDay { get; set; } = true;
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public ICollection<AbsenceWeekday> Weekdays { get; set; } = [];
    public Guid? ReplacesId { get; set; }
    public AbsenceRequest? Replaces { get; set; }
    public long? ReplacesVersion { get; set; }
    public long Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
public sealed class AbsenceWeekday
{
    public Guid AbsenceRequestId { get; set; }
    public AbsenceRequest AbsenceRequest { get; set; } = null!;
    public int Day { get; set; }
}
public sealed class AbsenceDecision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AbsenceRequestId { get; set; }
    public AbsenceRequest AbsenceRequest { get; set; } = null!;
    public Guid ActorUserId { get; set; }
    public ApplicationUser ActorUser { get; set; } = null!;
    public string Decision { get; set; } = "APPROVED";
    public string? RejectionReason { get; set; }
    public DateTime OccurredAt { get; set; }
}
public sealed class AbsenceConflict
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AbsenceRequestId { get; set; }
    public AbsenceRequest AbsenceRequest { get; set; } = null!;
    public Guid SchedulePeriodId { get; set; }
    public SchedulePeriod SchedulePeriod { get; set; } = null!;
    public Guid PublicationId { get; set; }
    public SchedulePublication Publication { get; set; } = null!;
    // Historical assignment identity deliberately has no FK to deletable live work.
    public Guid AssignmentId { get; set; }
    public DateOnly Date { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public string ShiftName { get; set; } = "";
    public DateTime? ResolvedAt { get; set; }
    public Guid? ResolvedByPublicationId { get; set; }
    public SchedulePublication? ResolvedByPublication { get; set; }
}
