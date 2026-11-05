namespace ShiftManager.Domain;

// Mutable controls are separate from the database-enforced immutable publication.
public sealed class PublicationControl
{
    public Guid PublicationId { get; set; }
    public bool Hidden { get; set; }
    public DateTime? DeletedAt { get; set; }
    public long Version { get; set; } = 1;
}

public sealed class ShiftExchange
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SchedulePeriodId { get; set; }
    public Guid RequesterUserId { get; set; }
    public Guid ReplacementUserId { get; set; }
    public string Status { get; set; } = "AWAITING_REPLACEMENT";
    public string? Reason { get; set; }
    public long Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ICollection<ShiftExchangeAssignment> Assignments { get; set; } = [];
}

public sealed class ShiftExchangeAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ShiftExchangeId { get; set; }
    public Guid PublicationId { get; set; }
    // Snapshot identities survive replacement of live draft assignments.
    public Guid SourceAssignmentId { get; set; }
    public Guid DefinitionId { get; set; }
    public DateOnly Date { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public bool IsShiftLead { get; set; }
    public string? Note { get; set; }
    public Guid? ReplacementAssignmentId { get; set; }
    public Guid? AppliedPublicationId { get; set; }
}

// One active request per source assignment, including both halves of on-call pairs.
public sealed class ShiftExchangeReservation
{
    public Guid AssignmentId { get; set; }
    public Guid ShiftExchangeId { get; set; }
}
