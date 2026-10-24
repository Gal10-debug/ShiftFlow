namespace ShiftManager.Domain;

public class Shift
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SchedulePeriodId { get; set; }
    public DateOnly ShiftDate { get; set; }
    public ShiftType ShiftType { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public Guid? CoordinatorUserId { get; set; }
    public SchedulePeriod SchedulePeriod { get; set; } = null!;
    public ApplicationUser? CoordinatorUser { get; set; }
    public ICollection<ShiftAssignment> Assignments { get; set; } = [];
    public ICollection<ShiftPreference> Preferences { get; set; } = [];
    public ICollection<ShiftTask> Tasks { get; set; } = [];
}
