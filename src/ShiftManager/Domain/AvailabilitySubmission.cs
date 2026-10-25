namespace ShiftManager.Domain;

public sealed class AvailabilitySubmission
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid SchedulePeriodId { get; set; }
    public DateTime LastSubmittedAt { get; set; }
    public long Version { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public SchedulePeriod SchedulePeriod { get; set; } = null!;
}
