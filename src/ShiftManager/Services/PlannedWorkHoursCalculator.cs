using ShiftManager.Api;
using ShiftManager.Services.AutomaticScheduling;

namespace ShiftManager.Services;

/// <summary>Pure calculation over one authorized immutable version; never reads live assignments.</summary>
public static class PlannedWorkHoursCalculator
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Jerusalem");
    private static Guid Definition(int slot) => Guid.Parse($"40000000-0000-0000-0000-{slot:000000000000}");

    public static PersonalScheduleResponse Calculate(ScheduleSnapshot snapshot, Guid employeeId, int year, int month)
    {
        AvailabilityService.MonthStart(year, month);
        if (snapshot.Year != year || snapshot.Month != month)
            throw new ArgumentException("Snapshot must belong to the reporting month.", nameof(snapshot));

        string Category(SnapshotCell c) => c.Semantic == SchedulingRules.OnCall ? "ON_CALL"
            : c.DefinitionId == Definition(14) ? "AVAILABILITY"
            : c.Semantic == SchedulingRules.SixNine ? "SIX_NINE"
            : c.DefinitionId == Definition(8) ? "REHABILITATION"
            : c.DefinitionId == Definition(12) || c.DefinitionId == Definition(13) ? "EARLY_END" : "TIMED_WORK";

        var raw = snapshot.Cells.SelectMany(c => c.Employees.Where(e => e.UserId == employeeId)
            .Select(e => Assignment(e.AssignmentId, c.DefinitionId, c.Date, c.Name, c.Period,
                Category(c), c.StartsAt, c.EndsAt, e.IsShiftLead)))
            .Concat(snapshot.LegacyAssignments.Where(a => a.UserId == employeeId)
                .Select(a => Assignment(a.AssignmentId, null, a.Date, a.Period == "DAY" ? "יום" : a.Period == "NIGHT" ? "לילה" : "משמרת היסטורית", a.Period,
                    "TIMED_WORK", a.StartsAt, a.EndsAt, a.IsShiftLead)))
            .Where(a => a.Date.Year == year && a.Date.Month == month);

        var assignments = raw.GroupBy(a => a.AssignmentId).Select(g => {
            var a = g.First();
            return g.Distinct().Count() == 1 ? a : a with { StartsAt = null, EndsAt = null,
                PlannedMinutes = null, DurationUnavailableReason = "CONFLICTING_DUPLICATE" };
        }).OrderBy(a => a.Date).ThenBy(a => a.StartsAt).ThenBy(a => a.AssignmentId).ToArray();

        PlannedHoursBreakdown Breakdown(string key, string name, IEnumerable<PersonalAssignment> items)
        {
            var rows = items.ToArray();
            return new(key, name, rows.Length, rows.Sum(a => (long)(a.PlannedMinutes ?? 0)), rows.Count(a => a.PlannedMinutes is null));
        }
        var summary = new PlannedHoursSummary(year, month, assignments.Length,
            assignments.Sum(a => (long)(a.PlannedMinutes ?? 0)), assignments.Count(a => a.PlannedMinutes is null),
            assignments.Count(a => a.Category is not ("ON_CALL" or "AVAILABILITY") && a.Period == "DAY"),
            assignments.Count(a => a.Category is not ("ON_CALL" or "AVAILABILITY") && a.Period == "NIGHT"),
            assignments.Where(a => a.Category == "ON_CALL").Select(a => (a.DefinitionId, Friday: SchedulingRules.Friday(a.Date))).Distinct().Count(),
            assignments.GroupBy(a => a.DefinitionId?.ToString() ?? $"LEGACY_{a.Period}")
                .Select(g => Breakdown(g.Key, g.First().Name, g)).ToArray(),
            assignments.GroupBy(a => a.Category).Select(g => Breakdown(g.Key, g.Key, g)).ToArray());
        return new(assignments, summary);
    }

    private static PersonalAssignment Assignment(Guid id, Guid? definition, DateOnly date, string name,
        string period, string category, DateTime start, DateTime end, bool lead)
    {
        var valid = start.Kind == DateTimeKind.Utc && end.Kind == DateTimeKind.Utc && end > start
            && (end - start).Ticks % TimeSpan.TicksPerMinute == 0
            && (end - start).Ticks / TimeSpan.TicksPerMinute <= int.MaxValue
            && DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(start, Zone)) == date;
        var reason = category == "ON_CALL" ? "ON_CALL_WORK_DURATION_UNDEFINED"
            : category == "AVAILABILITY" ? "AVAILABILITY_WORK_DURATION_UNDEFINED"
            : !valid ? "INVALID_INTERVAL" : null;
        return new(id, definition, date, name, period, category, valid ? start : null, valid ? end : null,
            valid && DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(end, Zone)) > date,
            reason is null ? (int)((end - start).Ticks / TimeSpan.TicksPerMinute) : null, reason, lead);
    }
}
