namespace ShiftManager.Api;

// Durations are elapsed minutes. Null totals distinguish incomplete data from zero work.
public sealed record PersonalAssignment(Guid AssignmentId, Guid? DefinitionId, DateOnly Date, string Name,
    string Period, string Category, DateTime? StartsAt, DateTime? EndsAt, bool Overnight,
    int? PlannedMinutes, string? DurationUnavailableReason, bool IsShiftLead);

public sealed record PlannedHoursBreakdown(string Key, string Name, int AssignedShifts,
    long KnownPlannedMinutes, int UnknownDurationAssignments)
{
    public bool IsComplete => UnknownDurationAssignments == 0;
    public long? TotalPlannedMinutes => IsComplete ? KnownPlannedMinutes : null;
    public decimal KnownPlannedHours => KnownPlannedMinutes / 60m;
    public decimal? TotalPlannedHours => TotalPlannedMinutes / 60m;
}

public sealed record PlannedHoursSummary(int Year, int Month, int TotalAssignedShifts,
    long KnownPlannedMinutes, int UnknownDurationAssignments, int DayShiftCount, int NightShiftCount,
    int OnCallDutyCount, PlannedHoursBreakdown[] ByShiftType, PlannedHoursBreakdown[] ByCategory)
{
    public bool IsComplete => UnknownDurationAssignments == 0;
    public long? TotalPlannedMinutes => IsComplete ? KnownPlannedMinutes : null;
    public decimal KnownPlannedHours => KnownPlannedMinutes / 60m;
    public decimal? TotalPlannedHours => TotalPlannedMinutes / 60m;
}

public sealed record PersonalScheduleResponse(PersonalAssignment[] Assignments, PlannedHoursSummary Summary,
    string TimeZone = "Asia/Jerusalem", string AttributionPolicy = "ASSIGNMENT_DATE");
