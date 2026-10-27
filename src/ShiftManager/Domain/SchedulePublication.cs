namespace ShiftManager.Domain;

public class SchedulePublication
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SchedulePeriodId { get; set; }
    public Guid PublishedByUserId { get; set; }
    public DateTime PublishedAt { get; set; }
    public string PublicationType { get; set; } = "LEGACY";
    public string Snapshot { get; set; } = "null";
    public bool AcknowledgedShortages { get; set; }
    public bool AcknowledgedOpenIssues { get; set; }
    public int OpenIssueCount { get; set; }
    public string? Notes { get; set; }
    public string InputFingerprint { get; set; } = "";
    public int VersionNumber { get; set; }
    public SchedulePeriod SchedulePeriod { get; set; } = null!;
    public ApplicationUser PublishedByUser { get; set; } = null!;
}
