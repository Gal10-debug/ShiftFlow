namespace ShiftManager.Domain;

public class ShiftPreference
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid ShiftId { get; set; }
    public PreferenceType PreferenceType { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public ApplicationUser User { get; set; } = null!;
    public Shift Shift { get; set; } = null!;
}
