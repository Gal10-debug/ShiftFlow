namespace ShiftManager.Api;

public sealed record AnalyticsTotals(int TotalAssignedShifts, long KnownPlannedMinutes,
    int UnknownDurationAssignments, int DayShiftCount, int NightShiftCount, int WeekendShiftCount,
    int SpecialDutyAssignments, PlannedHoursBreakdown[] ByShiftType)
{
    public bool IsComplete => UnknownDurationAssignments == 0;
    public long? TotalPlannedMinutes => IsComplete ? KnownPlannedMinutes : null;
}
public sealed record AnalyticsPublication(Guid Id, int VersionNumber, string PublicationType);
public sealed record AnalyticsMonth(int Year, int Month, AnalyticsPublication? Publication,
    AnalyticsTotals Totals, StaffingCoverage? Coverage = null);
public sealed record AnalyticsComparison(AnalyticsMonth PreviousMonth, int? ShiftCountChange,
    long? KnownPlannedMinutesChange, bool? BothDurationsComplete);
public sealed record PersonalAnalytics(int Year, int? Month, AnalyticsTotals Totals,
    AnalyticsMonth[] Months, AnalyticsComparison? Comparison,
    string TimeZone = "Asia/Jerusalem", string AttributionPolicy = "ASSIGNMENT_DATE");
public sealed record StaffingShortage(string Key, string Name, int RequiredPositions, int FilledPositions, int UnfilledPositions);
public sealed record StaffingCoverage(int RequiredPositions, int FilledPositions, int UnfilledPositions,
    StaffingShortage[] ByShiftType)
{
    public decimal? CoveragePercentage => RequiredPositions == 0 ? null : decimal.Round(100m * FilledPositions / RequiredPositions, 2);
}
public sealed record EmployeeWorkload(Guid EmployeeId, string Name, Guid? TeamId, AnalyticsTotals Totals);
public sealed record TeamWorkload(Guid? TeamId, string Name, int ScheduledEmployees, AnalyticsTotals Totals);
public sealed record ShiftDistribution(int AssignedShifts, int Employees);
public sealed record WorkloadDistribution(int ScheduledEmployees, int IncludedEmployees, int MinimumAssignedShifts,
    int MaximumAssignedShifts, decimal AverageAssignedShifts, decimal AverageShiftsPerScheduledEmployee,
    ShiftDistribution[] ShiftCounts);
public sealed record AdminAnalytics(int Year, int? Month, Guid? TeamId, Guid? EmployeeId,
    AnalyticsTotals Totals, AnalyticsMonth[] Months, StaffingCoverage? Coverage,
    WorkloadDistribution Distribution, EmployeeWorkload[] Employees, TeamWorkload[] Teams,
    string TeamAttributionPolicy = "CURRENT_MEMBERSHIP", string CoverageScope = "ORGANIZATION_ONLY",
    string AttributionPolicy = "ASSIGNMENT_DATE", string TimeZone = "Asia/Jerusalem");
