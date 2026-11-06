using ShiftManager.Api;
using ShiftManager.Domain;
using ShiftManager.Services;
using ShiftManager.Services.AutomaticScheduling;

namespace ShiftManager.Tests;

// Test oracle: intentionally does not call the optimizer, SchedulingRules,
// SchedulingProblem eligibility/conflict helpers, AbsenceRules or draft validation.
internal static class IndependentScheduleValidator
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Jerusalem");
    internal static Guid Definition(int slot) => Guid.Parse($"40000000-0000-0000-0000-{slot:000000000000}");
    internal static bool Weekend(DateOnly date) => date.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday;
    internal static DateOnly Week(DateOnly date) => date.AddDays(-(int)date.DayOfWeek);
    private static DateOnly Friday(DateOnly date) => date.DayOfWeek == DayOfWeek.Saturday ? date.AddDays(-1) : date;
    private static bool Applicable(Guid? definition, string semantic, DateOnly date) =>
        definition == Definition(8) ? date.DayOfWeek != DayOfWeek.Saturday :
        semantic == "WEEKEND_ON_CALL" ? Weekend(date) :
        !Weekend(date) || new[] { 2, 3, 4, 5, 16 }.Any(i => definition == Definition(i));
    private static bool Overlap(DateTime start, DateTime end, DateTime otherStart, DateTime otherEnd) => start < otherEnd && otherStart < end;
    internal static DateTime Utc(DateOnly date, int hour) => TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(new TimeOnly(hour, 0)), Zone);
    private static DateTime Boundary(DateTime wall, bool end)
    {
        while (Zone.IsInvalidTime(wall)) wall = wall.AddMinutes(1);
        if (!Zone.IsAmbiguousTime(wall)) return TimeZoneInfo.ConvertTimeToUtc(wall, Zone);
        var offsets = Zone.GetAmbiguousTimeOffsets(wall);
        return new DateTimeOffset(wall, end ? offsets.Min() : offsets.Max()).UtcDateTime;
    }
    internal static bool Absent(AbsenceRule rule, DateTime start, DateTime end)
    {
        // Enumerate local dates around the actual interval, including an overnight absence.
        var first = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(start, Zone)).AddDays(-1);
        var last = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(end, Zone));
        for (var date = first; date <= last; date = date.AddDays(1))
        {
            if (date < rule.StartDate || rule.EndDate.HasValue && date > rule.EndDate.Value
                || rule.Recurring && !rule.Weekdays.Contains((int)date.DayOfWeek)) continue;
            var from = date.ToDateTime(rule.FullDay ? TimeOnly.MinValue : rule.StartTime!.Value);
            var until = rule.FullDay ? date.AddDays(1).ToDateTime(TimeOnly.MinValue) :
                (rule.EndTime <= rule.StartTime ? date.AddDays(1) : date).ToDateTime(rule.EndTime!.Value);
            if (Overlap(start, end, Boundary(from, false), Boundary(until, true))) return true;
        }
        return false;
    }
    private static (Guid? Definition, DateOnly Date) Allocation(Work w) => w.Semantic == "WEEKEND_ON_CALL"
        ? (w.DefinitionId, Friday(w.Date)) : (w.DefinitionId, w.Date);
    internal static int WeekendAllocations(IEnumerable<Work> work) => work.Where(w => Weekend(w.Date))
        .GroupBy(w => w.Semantic == "WEEKEND_ON_CALL" ? $"pair/{w.DefinitionId}/{Friday(w.Date)}" : $"work/{w.AssignmentId}").Count();

    internal static string[] Check(SchedulingProblem p, GeneratedCell[] result)
    {
        var errors = new HashSet<string>();
        var committed = p.Work.Where(w => !w.Replaceable).ToList();
        var keys = p.Cells.Select(c => (c.DefinitionId, c.Date)).ToHashSet();
        if (result.Length != keys.Count || result.Select(c => (c.DefinitionId, c.Date)).Distinct().Count() != result.Length
            || result.Any(c => !keys.Contains((c.DefinitionId, c.Date)))) errors.Add("CELL_SET");
        foreach (var c in p.Cells)
        {
            var output = result.FirstOrDefault(o => o.DefinitionId == c.DefinitionId && o.Date == c.Date);
            if (output is null) { errors.Add("CELL_SET"); continue; }
            if (output.StartsAt != c.Start || output.EndsAt != c.End || output.RequiredEmployees != c.Required) errors.Add("CELL_METADATA");
            if (output.Employees.Length > c.Required || c.Required == 0 && output.Employees.Length != 0) errors.Add("CAPACITY");
            if (output.Employees.Select(e => e.UserId).Distinct().Count() != output.Employees.Length) errors.Add("DUPLICATE");
            var originals = committed.Where(w => w.DefinitionId == c.DefinitionId && w.Date == c.Date).ToArray();
            foreach (var original in originals)
                if (!output.Employees.Any(e => e.Protected && e.UserId == original.UserId && e.AssignmentId == original.AssignmentId
                    && e.IsShiftLead == original.Lead && e.Note == original.Note)) errors.Add("PROTECTED");
            foreach (var member in output.Employees)
            {
                var employee = p.Employees.FirstOrDefault(e => e.Id == member.UserId);
                if (employee is null || employee.IsTeamLeader) errors.Add("ELIGIBILITY");
                if (c.RequiresShiftLeadQualification && employee?.IsShiftLeadQualified != true) errors.Add("QUALIFICATION");
                if (member.IsShiftLead && employee?.IsShiftLeadQualified != true) errors.Add("QUALIFICATION");
                if (member.Protected)
                {
                    if (!originals.Any(w => w.UserId == member.UserId && w.AssignmentId == member.AssignmentId)) errors.Add("PROTECTED");
                }
                else
                {
                    if (c.AssignmentMode != AssignmentMode.Automatic) errors.Add("MANUAL_ONLY");
                    committed.Add(new(Guid.NewGuid(), member.UserId, c.DefinitionId, c.Date, c.Start, c.End,
                        Guid.Empty, false, member.IsShiftLead, c.AnchorType, c.Semantic));
                }
            }
        }
        bool Current(Work w) => w.Date.Year == p.Year && w.Date.Month == p.Month;
        foreach (var w in committed.Where(Current))
        {
            if (!Applicable(w.DefinitionId, w.Semantic, w.Date)) errors.Add("APPLICABILITY");
            if ((p.ApprovedAbsences ?? []).Any(a => a.UserId == w.UserId && Absent(a, w.Start, w.End))) errors.Add("ABSENCE");
            foreach (var pref in p.Preferences.Where(b => b.UserId == w.UserId && b.State == PreferenceType.BLOCKED))
            {
                var start = Utc(pref.Date, pref.Type == ShiftType.DAY ? 7 : 19);
                var end = Utc(pref.Type == ShiftType.DAY ? pref.Date : pref.Date.AddDays(1), pref.Type == ShiftType.DAY ? 19 : 7);
                if (Overlap(w.Start, w.End, start, end)) errors.Add("AVAILABILITY");
            }
        }
        foreach (var user in committed.GroupBy(w => w.UserId))
        {
            var rows = user.OrderBy(w => w.Start).ToArray();
            for (var i = 0; i < rows.Length; i++)
                for (var j = i + 1; j < rows.Length; j++)
                    if ((Current(rows[i]) || Current(rows[j])) && rows[i].Start <= rows[j].End && rows[j].Start <= rows[i].End) errors.Add("OVERLAP_OR_TOUCH");
            foreach (var anchor in rows.GroupBy(w => (w.Date, w.AnchorType)))
                if (anchor.Count() > 1 && anchor.Any(Current)) errors.Add("ANCHOR_DUPLICATE");
            foreach (var week in rows.GroupBy(w => Week(w.Date)).Where(g => g.Any(Current)))
            {
                if (week.Count(w => w.AnchorType == ShiftType.NIGHT) > 2) errors.Add("WEEKLY_NIGHT");
                if (week.Count(w => w.Semantic == "SIX_NINE") > 1) errors.Add("WEEKLY_SIX_NINE");
            }
            foreach (var month in rows.Where(w => Weekend(w.Date)).GroupBy(w => {
                var date = Allocation(w).Date; return (date.Year, date.Month);
            }).Where(g => g.Any(Current)))
                if (WeekendAllocations(month) > 2) errors.Add("MONTHLY_WEEKEND");
            foreach (var pair in rows.Where(w => w.Semantic == "WEEKEND_ON_CALL").GroupBy(Allocation).Where(g => g.Any(Current)))
                if (pair.Count(w => w.Date == pair.Key.Date) != 1 || pair.Count(w => w.Date == pair.Key.Date.AddDays(1)) != 1) errors.Add("ON_CALL_PAIR");
        }
        foreach (var c in p.StaffingCells ?? p.Cells)
            if (committed.Count(w => w.DefinitionId == c.DefinitionId && w.Date == c.Date) > c.Required) errors.Add("CAPACITY");
        return errors.Order().ToArray();
    }
}
