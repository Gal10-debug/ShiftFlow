namespace ShiftManager.Domain;

public sealed class OrganizationalTeam
{
    public Guid Id { get; set; }
    public int Slot { get; set; }
    public string Name { get; set; } = "";
    public Guid? LeaderUserId { get; set; }
    public Guid? DeputyUserId { get; set; }
    public long Version { get; set; } = 1;
}
public sealed class TeamMembership
{
    public Guid UserId { get; set; }
    public Guid TeamId { get; set; }
}
public sealed class DelegatedAvailabilityAudit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ActorUserId { get; set; }
    public Guid TargetUserId { get; set; }
    public Guid TeamId { get; set; }
    public Guid SchedulePeriodId { get; set; }
    public DateTime OccurredAt { get; set; }
    public bool InitialSubmission { get; set; }
    public string PreviousValues { get; set; } = "[]";
    public string NewValues { get; set; } = "[]";
}
