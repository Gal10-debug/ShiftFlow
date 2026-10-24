namespace ShiftManager.Domain;

public class TaskAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ShiftTaskId { get; set; }
    public Guid ShiftAssignmentId { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public ShiftTask ShiftTask { get; set; } = null!;
    public ShiftAssignment ShiftAssignment { get; set; } = null!;
}
