using ShiftManager.Api;
using ShiftManager.Services.AutomaticScheduling;

namespace ShiftManager.Services;

/// <summary>Deterministic aggregation of already authorized immutable publications.</summary>
public static class SchedulingAnalyticsCalculator
{
    public static AnalyticsTotals Empty => new(0, 0, 0, 0, 0, 0, 0, []);

    public static AnalyticsTotals Totals(PersonalScheduleResponse personal) => new(
        personal.Summary.TotalAssignedShifts, personal.Summary.KnownPlannedMinutes,
        personal.Summary.UnknownDurationAssignments, personal.Summary.DayShiftCount, personal.Summary.NightShiftCount,
        personal.Assignments.Count(a => SchedulingRules.Weekend(a.Date)),
        personal.Assignments.Count(a => a.Category != "TIMED_WORK"), personal.Summary.ByShiftType);

    public static AnalyticsTotals Sum(IEnumerable<AnalyticsTotals> source)
    {
        var rows = source.ToArray();
        return new(rows.Sum(r => r.TotalAssignedShifts), rows.Sum(r => r.KnownPlannedMinutes),
            rows.Sum(r => r.UnknownDurationAssignments), rows.Sum(r => r.DayShiftCount), rows.Sum(r => r.NightShiftCount),
            rows.Sum(r => r.WeekendShiftCount), rows.Sum(r => r.SpecialDutyAssignments),
            rows.SelectMany(r => r.ByShiftType).GroupBy(r => r.Key).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new PlannedHoursBreakdown(g.Key, g.First().Name, g.Sum(r => r.AssignedShifts),
                    g.Sum(r => r.KnownPlannedMinutes), g.Sum(r => r.UnknownDurationAssignments))).ToArray());
    }

    // Partition once: the Phase 8A calculator never rescans the full roster for every employee.
    public static Dictionary<Guid, AnalyticsTotals> Employees(ScheduleSnapshot snapshot)
    {
        var cells = snapshot.Cells.SelectMany(c => c.Employees.Select(e => (e.UserId, Cell: c with { Employees = [e] })))
            .GroupBy(x => x.UserId).ToDictionary(g => g.Key, g => g.Select(x => x.Cell).ToArray());
        var legacy = snapshot.LegacyAssignments.GroupBy(a => a.UserId).ToDictionary(g => g.Key, g => g.ToArray());
        return cells.Keys.Union(legacy.Keys).Order().ToDictionary(id => id, id => Totals(PlannedWorkHoursCalculator.Calculate(
            new(snapshot.Year, snapshot.Month, cells.GetValueOrDefault(id) ?? [], legacy.GetValueOrDefault(id) ?? []),
            id, snapshot.Year, snapshot.Month)));
    }

    public static StaffingCoverage Coverage(ScheduleSnapshot snapshot)
    {
        var seen = new HashSet<Guid>();
        var cells = snapshot.Cells.Where(c => c.Date.Year == snapshot.Year && c.Date.Month == snapshot.Month
            && c.Applicable && c.AssignmentMode != "ManualOnly")
            .GroupBy(c => (c.DefinitionId, c.Date)).OrderBy(g => g.Key.Date).ThenBy(g => g.Key.DefinitionId)
            .Select(g => {
                var c = g.First();
                var required = Math.Max(0, c.RequiredEmployees);
                var filled = Math.Min(required, g.SelectMany(x => x.Employees).Select(e => e.AssignmentId).Distinct().Count(id => seen.Add(id)));
                return new StaffingShortage(c.DefinitionId.ToString(), c.Name, required, filled, required - filled);
            }).ToArray();
        return new(cells.Sum(c => c.RequiredPositions), cells.Sum(c => c.FilledPositions), cells.Sum(c => c.UnfilledPositions),
            cells.GroupBy(c => c.Key).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => new StaffingShortage(g.Key,
                g.First().Name, g.Sum(c => c.RequiredPositions), g.Sum(c => c.FilledPositions), g.Sum(c => c.UnfilledPositions))).ToArray());
    }
    public static StaffingCoverage? SumCoverage(IEnumerable<StaffingCoverage?> source)
    {
        var rows = source.OfType<StaffingCoverage>().ToArray();
        if (rows.Length == 0) return null;
        return new(rows.Sum(r => r.RequiredPositions), rows.Sum(r => r.FilledPositions), rows.Sum(r => r.UnfilledPositions),
            rows.SelectMany(r => r.ByShiftType).GroupBy(r => r.Key).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new StaffingShortage(g.Key, g.First().Name, g.Sum(r => r.RequiredPositions),
                    g.Sum(r => r.FilledPositions), g.Sum(r => r.UnfilledPositions))).ToArray());
    }
    public static WorkloadDistribution Distribution(IEnumerable<EmployeeWorkload> source)
    {
        var counts = source.Select(e => e.Totals.TotalAssignedShifts).ToArray();
        var scheduled = counts.Count(c => c > 0);
        return new(scheduled, counts.Length, counts.DefaultIfEmpty().Min(), counts.DefaultIfEmpty().Max(),
            counts.Length == 0 ? 0 : (decimal)counts.Sum() / counts.Length,
            scheduled == 0 ? 0 : (decimal)counts.Sum() / scheduled,
            counts.GroupBy(c => c).OrderBy(g => g.Key).Select(g => new ShiftDistribution(g.Key, g.Count())).ToArray());
    }
}
