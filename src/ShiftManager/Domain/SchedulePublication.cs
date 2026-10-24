namespace ShiftManager.Domain;

public class SchedulePublication
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SchedulePeriodId { get; set; }
    public Guid PublishedByUserId { get; set; }
    public DateTime PublishedAt { get; set; }
    public int VersionNumber { get; set; }
    public SchedulePeriod SchedulePeriod { get; set; } = null!;
    public ApplicationUser PublishedByUser { get; set; } = null!;
}
