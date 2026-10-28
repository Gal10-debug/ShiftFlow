using System.ComponentModel.DataAnnotations;
namespace ShiftManager.Api;

public sealed record ShiftTypeResponse(Guid Id, int Slot, string Name, int DisplayOrder, string Period,
    int RequiredEmployees, bool IncludeInAutomaticScheduling, long Version, DateTime UpdatedAt, string? TimeRange = null, bool RequiresShiftLeadQualification = false, string Semantic = "ORDINARY");
public sealed record ShiftTypeUpdate(Guid Id, [Required, Range(1, long.MaxValue)] long? ExpectedVersion,
    [Required, StringLength(100), RegularExpression(@".*\S.*")] string Name,
    [Required, Range(1, 51)] int? DisplayOrder, [Required] string Period,
    [Required, Range(0, 1000)] int? RequiredEmployees, [Required] bool? IncludeInAutomaticScheduling, string? TimeRange = null, bool RequiresShiftLeadQualification = false);
public sealed record UpdateShiftTypesRequest([Required, MinLength(51), MaxLength(51)] ShiftTypeUpdate[] ShiftTypes);
public sealed record BoardDayResponse(DateOnly Date, int Year, int Month, Guid? SchedulePeriodId);
public sealed record AvailabilityAnchorResponse(DateOnly Date, string Period, Guid? ShiftId);
public sealed record BoardCellResponse(DateOnly Date, Guid? ShiftId, DateTime StartsAt, DateTime EndsAt, string State, AvailabilityAnchorResponse[]? AvailabilityAnchors = null, ScheduledEmployee[]? Employees = null, int? RequiredEmployees = null, int? Shortage = null);
public sealed record BoardRowResponse(ShiftTypeResponse Definition, BoardCellResponse[] Cells);
public sealed record WeeklyBoardResponse(DateOnly WeekStartsOn, DateOnly WeekEndsOn, string TimeZone,
    string WorkspaceState, BoardDayResponse[] Days, BoardRowResponse[] Rows);
public sealed record AutomaticSchedulingConfigurationResponse(string TimeZone, string WorkspaceState,
    ShiftTypeResponse[] Rows, Guid[] ExcludedDefinitionIds);
