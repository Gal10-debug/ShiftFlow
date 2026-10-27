namespace ShiftManager.Domain;
public sealed class ScheduleIssue
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SchedulePeriodId { get; set; }
    public Guid PublicationId { get; set; }
    public Guid AssignmentId { get; set; } // Immutable snapshot identity, deliberately not a live-assignment FK.
    public Guid ReporterUserId { get; set; }
    public string Explanation { get; set; } = "";
    public string Status { get; set; } = "OPEN";
    public string? Response { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public long Version { get; set; } = 1;
}
public sealed class ScheduleEditAudit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SchedulePeriodId { get; set; }
    public Guid ActorUserId { get; set; }
    public DateTime OccurredAt { get; set; }
    public string Operation { get; set; } = "";
    public string Before { get; set; } = "null";
    public string After { get; set; } = "null";
}
