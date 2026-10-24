namespace ShiftManager.Domain;

public class AvailabilityWindow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SchedulePeriodId { get; set; }
    public WindowStatus Status { get; set; }
    public DateTime OpensAt { get; set; }
    public DateTime? ClosesAt { get; set; }
    public Guid OpenedByUserId { get; set; }
    public Guid? ClosedByUserId { get; set; }
    public SchedulePeriod SchedulePeriod { get; set; } = null!;
    public ApplicationUser OpenedByUser { get; set; } = null!;
    public ApplicationUser? ClosedByUser { get; set; }
}
