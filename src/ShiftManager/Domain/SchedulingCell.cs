namespace ShiftManager.Domain;

// Canonical Shift remains the availability/task anchor; a cell owns the actual work interval.
public sealed class SchedulingCell
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ShiftTypeDefinitionId { get; set; }
    public ShiftTypeDefinition Definition { get; set; } = null!;
    public Guid SchedulePeriodId { get; set; }
    public SchedulePeriod SchedulePeriod { get; set; } = null!;
    public DateOnly Date { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public int RequiredEmployees { get; set; }
    public long ConfigurationVersion { get; set; }
    public Guid SchedulingRunId { get; set; }
    public SchedulingRun Run { get; set; } = null!;
    public ICollection<ShiftAssignment> Assignments { get; set; } = [];
}

// Only explicit saves persist a run. Previews are bounded, ephemeral server-side objects.
public sealed class SchedulingRun
{
    public Guid Id { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public DateTime SavedAt { get; set; }
    public Guid SavedByUserId { get; set; }
    public ApplicationUser SavedByUser { get; set; } = null!;
    public string InputFingerprint { get; set; } = "";
    public string SolverStatus { get; set; } = "";
    public int ProvenObjectives { get; set; }
    public int Required { get; set; }
    public int Filled { get; set; }
    public int Missing { get; set; }
    public int UnderstaffedCells { get; set; }
    public int FullyStaffedCells { get; set; }
}
