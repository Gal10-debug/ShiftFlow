namespace ShiftManager.Domain;

// A configurable board row inside an existing DAY/NIGHT Shift, not an employee slot.
public sealed class ShiftTypeDefinition
{
    public Guid Id { get; set; }
    public int Slot { get; set; }
    public string Name { get; set; } = "";
    public int DisplayOrder { get; set; }
    public ShiftType Period { get; set; }
    public int RequiredEmployees { get; set; }
    public bool IncludeInAutomaticScheduling { get; set; }
    public long Version { get; set; } = 1;
    public DateTime UpdatedAt { get; set; }
}
