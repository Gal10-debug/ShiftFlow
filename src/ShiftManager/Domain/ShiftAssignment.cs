namespace ShiftManager.Domain;

public class ShiftAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ShiftId { get; set; }
    public Guid UserId { get; set; }
    public bool IsShiftLead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ApplicationUser User { get; set; } = null!;
    public Shift Shift { get; set; } = null!;
    public ICollection<TaskAssignment> Tasks { get; set; } = [];
}
