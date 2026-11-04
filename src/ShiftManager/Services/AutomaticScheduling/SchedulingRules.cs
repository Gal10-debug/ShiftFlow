using ShiftManager.Domain;
namespace ShiftManager.Services.AutomaticScheduling;

public sealed record SchedulingViolation(string Code, Guid UserId, DateOnly Date, Guid[] AssignmentIds);

// Shared allocation keys and validation for CP-SAT, saved previews, manual edits and publication.
public static class SchedulingRules
{
    public const string SixNine = "SIX_NINE";
    public const string OnCall = "WEEKEND_ON_CALL";
    public static bool Weekend(DateOnly date) => date.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday;
    public static DateOnly Friday(DateOnly date) => date.DayOfWeek == DayOfWeek.Saturday ? date.AddDays(-1) : date;
    public static DateOnly AllocationMonth(Work w) => Month(w.Semantic == OnCall ? Friday(w.Date) : w.Date);
    public static DateOnly Month(DateOnly date) => new(date.Year, date.Month, 1);
    public static DateOnly Week(DateOnly date) => AvailabilityService.WeekStart(date);
    // Immutable seeded identities confirmed against the demo board: slots 2, 3, 4, 5, 16.
    private static readonly HashSet<Guid> WeekendDefinitions = new[] { 2, 3, 4, 5, 16 }
        .Select(slot => Guid.Parse($"40000000-0000-0000-0000-{slot:000000000000}")).ToHashSet();
    public static readonly Guid RehabilitationMorning = Guid.Parse("40000000-0000-0000-0000-000000000008");
    private static readonly Guid Row3 = Guid.Parse("40000000-0000-0000-0000-000000000003");
    private static bool WeekendAllowed(Guid? definition, string semantic) => semantic == OnCall || definition.HasValue && WeekendDefinitions.Contains(definition.Value);
    public static bool Applicable(Guid? definition, string semantic, DateOnly date) =>
        definition == RehabilitationMorning ? date.DayOfWeek != DayOfWeek.Saturday :
        semantic == OnCall ? Weekend(date) : !Weekend(date) || WeekendAllowed(definition, semantic);
    public static int WeekendRequired(string semantic, int required, Guid? definition = null) =>
        !WeekendAllowed(definition, semantic) ? 0 : definition == Row3 ? 1 : required;
    public static int Required(string semantic, DateOnly date, int required, Guid? definition = null) =>
        definition == RehabilitationMorning ? date.DayOfWeek == DayOfWeek.Saturday ? 0 : required :
        Weekend(date) ? WeekendRequired(semantic, required, definition) : semantic == OnCall ? 0 : required;
    public static bool HasDemand(Guid definition, string semantic, int required) => required > 0 || WeekendRequired(semantic, required, definition) > 0;
    public static Work Proposed(Guid user, CandidateCell c) => new(Guid.NewGuid(), user, c.DefinitionId, c.Date, c.Start, c.End, Guid.Empty, false, false, c.AnchorType, c.Semantic);
    public static int Allocations(IEnumerable<Work> work) => work.Count(w => w.Semantic != OnCall)
        + work.Where(w => w.Semantic == OnCall).Select(w => (w.DefinitionId, Friday(w.Date))).Distinct().Count();

    public static SchedulingViolation[] Violations(IEnumerable<Work> source, int year, int month, bool pairing = true)
    {
        var work = source.ToArray(); var result = new List<SchedulingViolation>();
        bool Touches(IEnumerable<Work> group) => group.Any(w => w.Date.Year == year && w.Date.Month == month);
        void Add(string code, IEnumerable<Work> group)
        {
            var rows = group.ToArray(); if (!Touches(rows)) return;
            result.Add(new(code, rows[0].UserId, rows.Min(w => w.Date), rows.Select(w => w.AssignmentId).ToArray()));
        }
        foreach (var w in work.Where(w => !Applicable(w.DefinitionId, w.Semantic, w.Date)))
            Add(w.Semantic == OnCall ? "ON_CALL_WEEKEND_ONLY" : "WEEKEND_SHIFT_PROHIBITED", new[] { w });
        foreach (var g in work.Where(w => Weekend(w.Date)).GroupBy(w => (w.UserId, Month: AllocationMonth(w))))
            if (Allocations(g) > 2) Add("WEEKEND_ALLOCATION_LIMIT", g);
        foreach (var g in work.Where(w => w.Semantic == SixNine).GroupBy(w => (w.UserId, Week: Week(w.Date))))
            if (g.Count() > 1) Add("SIX_NINE_WEEKLY_LIMIT", g);
        foreach (var g in work.Where(w => w.AnchorType == ShiftType.NIGHT).GroupBy(w => (w.UserId, Week: Week(w.Date))))
            if (g.Count() > 2) Add("NIGHT_WEEKLY_LIMIT", g);
        if (pairing)
        {
            foreach (var g in work.Where(w => w.Semantic == OnCall && !Weekend(w.Date)).GroupBy(w => w.UserId)) Add("ON_CALL_WEEKEND_ONLY", g);
            foreach (var g in work.Where(w => w.Semantic == OnCall && Weekend(w.Date)).GroupBy(w => (w.UserId, w.DefinitionId, Friday: Friday(w.Date))))
                if (g.Count(w => w.Date == g.Key.Friday) != 1 || g.Count(w => w.Date == g.Key.Friday.AddDays(1)) != 1) Add("ON_CALL_PAIR_REQUIRED", g);
        }
        return result.ToArray();
    }
    public static string[] CandidateReasons(SchedulingProblem p, CandidateCell cell, Guid user, bool pairing = true)
    {
        if (!Applicable(cell.DefinitionId, cell.Semantic, cell.Date)) return [cell.Semantic == OnCall ? "ON_CALL_WEEKEND_ONLY" : "WEEKEND_SHIFT_PROHIBITED"];
        var proposed = Proposed(user, cell);
        return Violations(p.Work.Append(proposed), cell.Date.Year, cell.Date.Month, pairing)
            .Where(v => v.UserId == user && v.AssignmentIds.Contains(proposed.AssignmentId)).Select(v => v.Code).Distinct().ToArray();
    }
}
