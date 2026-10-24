namespace ShiftManager.Domain;

public class SchedulePeriod
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Year { get; set; }
    public int Month { get; set; }
    public ScheduleStatus Status { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<Shift> Shifts { get; set; } = [];
    public AvailabilityWindow? AvailabilityWindow { get; set; }
    public ICollection<SchedulePublication> Publications { get; set; } = [];
}
