namespace ShiftManager.Domain;

// A configurable board row with a local time range, not an employee slot.
public sealed class ShiftTypeDefinition
{
    public string Semantic { get; set; } = "ORDINARY";
    public Guid Id { get; set; }
    public int Slot { get; set; }
    public string Name { get; set; } = "";
    public int DisplayOrder { get; set; }
    public ShiftType Period { get; set; }
    public ShiftTimeRange? TimeRange { get; set; }
    public int RequiredEmployees { get; set; }
    public bool IncludeInAutomaticScheduling { get; set; }
    public bool RequiresShiftLeadQualification { get; set; }
    public long Version { get; set; } = 1;
    public DateTime UpdatedAt { get; set; }
}
