using ShiftManager.Api;
using ShiftManager.Development;
using ShiftManager.Domain;
using ShiftManager.Services;
using ShiftManager.Services.AutomaticScheduling;

namespace ShiftManager.Tests;

internal static class SchedulingValidationScenarios
{
    internal const int Seed = 731;
    internal static Guid Id(int slot) => IndependentScheduleValidator.Definition(slot);
    internal static Employee Employee(int i, bool qualified = true) => new(Guid.Parse($"70000000-0000-0000-0000-{i:000000000000}"), $"Employee {i}", qualified);
    internal static CandidateCell Cell(int slot, DateOnly date, ShiftTimeRange range = ShiftTimeRange.DAY,
        int required = 1, bool qualified = false, string semantic = "ORDINARY", AssignmentMode mode = AssignmentMode.Automatic)
    {
        var times = ShiftTimeRanges.Boundaries(date, range);
        return new(Id(slot), date, times.StartsAt, times.EndsAt, required, 1, ShiftTimeRanges.Classification(range), qualified, semantic, mode);
    }
    internal static Work Work(CandidateCell c, Employee e, int id = 1, bool replaceable = false, bool lead = false) =>
        new(Guid.Parse($"71000000-0000-0000-0000-{id:000000000000}"), e.Id, c.DefinitionId, c.Date, c.Start, c.End,
            Guid.Empty, replaceable, lead, c.AnchorType, c.Semantic, replaceable ? null : "Protected fixture");
    internal static SchedulingProblem Problem(CandidateCell[] cells, Employee[]? employees = null, Work[]? work = null,
        Preference[]? preferences = null, AbsenceRule[]? absences = null) => new(cells[0].Date.Year, cells[0].Date.Month,
        "phase7a", cells, employees ?? [Employee(1)], work ?? [], preferences ?? [], ApprovedAbsences: absences);
    internal static GeneratedCell[] Assign(CandidateCell[] cells, Employee employee) => cells.Select(c =>
        new GeneratedCell(c.DefinitionId, c.Date, c.Start, c.End, c.Required, [new(employee.Id, employee.Name, false, false)])).ToArray();

    internal static SchedulingProblem Month(string scenario, int year, int month)
    {
        var first = new DateOnly(year, month, 1); var last = first.AddMonths(1).AddDays(-1);
        var random = new Random(Seed);
        var employees = DemoPlan.Accounts(Seed).Where(a => a.Position != "leader")
            .Select(a => new Employee(a.Id, a.Login, a.Qualified)).ToArray();
        if (scenario == "severe-shortage") employees = employees.Take(6).ToArray();
        if (scenario == "insufficient-leads") employees = employees.Select((e, i) => e with { IsShiftLeadQualified = i == 0 }).ToArray();
        // Stable supported identities with application-supported ranges. No production/demo data is read.
        var rows = new[] {
            (1, ShiftTimeRange.H06_09, 1, false, "SIX_NINE"),
            (2, ShiftTimeRange.DAY, 1, true, "ORDINARY"),
            (3, ShiftTimeRange.DAY, scenario == "variable-headcount" ? 0 : 2, false, "ORDINARY"),
            (4, ShiftTimeRange.NIGHT, 1, true, "ORDINARY"),
            (5, ShiftTimeRange.NIGHT, 2, false, "ORDINARY"),
            (6, ShiftTimeRange.H07_15, 1, false, "ORDINARY"),
            (7, ShiftTimeRange.H15_23, 1, false, "ORDINARY"),
            (8, ShiftTimeRange.H07_13, 1, false, "ORDINARY"),
            (12, ShiftTimeRange.H07_15, 1, false, "ORDINARY"),
            (13, ShiftTimeRange.H07_13, 1, false, "ORDINARY"),
            (14, ShiftTimeRange.H15_23, 1, false, "ORDINARY"),
            (16, ShiftTimeRange.DAY, 1, false, "WEEKEND_ON_CALL"),
            (26, ShiftTimeRange.H23_07, 1, false, "ORDINARY") };
        var staffing = rows.SelectMany(row => Enumerable.Range(0, last.Day).Select(i => {
            var date = first.AddDays(i);
            var required = SchedulingRules.Required(row.Item5, date, row.Item3, Id(row.Item1));
            return Cell(row.Item1, date, row.Item2, required, row.Item4, row.Item5,
                row.Item1 is 12 or 13 or 14 ? AssignmentMode.ManualOnly : AssignmentMode.Automatic) with { SchedulingPriority = row.Item1 <= 7 ? SchedulingPriority.Critical : SchedulingPriority.Normal };
        })).ToArray();
        var cells = staffing.Where(c => c.AssignmentMode == AssignmentMode.Automatic).ToArray();
        var preferences = new List<Preference>(); var absences = new List<AbsenceRule>();
        for (var i = 0; i < employees.Length; i++)
        {
            var employee = employees[i];
            if (scenario is "moderate-absences" or "heavily-constrained" && i % 3 == 0)
                absences.Add(new(Id(100 + i), employee.Id, first.AddDays(random.Next(0, 15)), first.AddDays(20), false, [], true, null, null));
            if (scenario == "widespread-absences" && i % 5 != 0)
                absences.Add(new(Id(200 + i), employee.Id, first, last, false, [], true, null, null));
            if (scenario is "studies" or "heavily-constrained" && i % 2 == 0)
                absences.Add(new(Id(300 + i), employee.Id, first, null, true, [1, 3], false, new(8, 0), new(16, 0)));
            if (scenario == "reserve-duty" && i < employees.Length / 3)
                absences.Add(new(Id(400 + i), employee.Id, first.AddDays(2), first.AddDays(22), false, [], true, null, null));
            // At most three blocked anchors per Sunday week, matching supported availability submissions.
            if (scenario is "overlapping-restrictions" or "heavily-constrained")
            {
                foreach (var week in cells.Select(c => IndependentScheduleValidator.Week(c.Date)).Distinct())
                {
                    var weekday = random.Next(0, 7);
                    var anchors = new[] { (weekday, ShiftType.DAY), (weekday, ShiftType.NIGHT), ((weekday + 1) % 7, ShiftType.DAY) };
                    preferences.AddRange(anchors.Select(a => new Preference(employee.Id, week.AddDays(a.Item1), a.Item2, PreferenceType.BLOCKED)));
                }
            }
        }
        return new(year, month, $"phase7a/{scenario}/{Seed}", cells, employees, [], preferences.ToArray(), staffing, absences.ToArray());
    }
}
