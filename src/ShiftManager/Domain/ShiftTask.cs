namespace ShiftManager.Domain;

public class ShiftTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ShiftId { get; set; }
    public Guid? TaskTemplateId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Shift Shift { get; set; } = null!;
    public TaskTemplate? TaskTemplate { get; set; }
    public ApplicationUser CreatedByUser { get; set; } = null!;
    public ICollection<TaskAssignment> Assignments { get; set; } = [];
}
