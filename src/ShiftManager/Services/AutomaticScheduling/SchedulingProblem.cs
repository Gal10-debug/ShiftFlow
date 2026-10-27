using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Api;
using ShiftManager.Data;
using ShiftManager.Domain;
namespace ShiftManager.Services.AutomaticScheduling;

public sealed record Work(Guid AssignmentId, Guid UserId, Guid? DefinitionId, DateOnly Date,
    DateTime Start, DateTime End, Guid ShiftId, bool Replaceable, bool Lead, ShiftType AnchorType);
public sealed record CandidateCell(Guid DefinitionId, DateOnly Date, DateTime Start, DateTime End,
    int Required, long Version, ShiftType AnchorType, bool RequiresShiftLeadQualification = false);
public sealed record Employee(Guid Id, string Name, bool IsShiftLeadQualified = false, bool IsTeamLeader = false);
public sealed record Preference(Guid UserId, DateOnly Date, ShiftType Type, PreferenceType State);
public sealed record SchedulingProblem(int Year, int Month, string Fingerprint, CandidateCell[] Cells,
    Employee[] Employees, Work[] Work, Preference[] Preferences)
{
    public static bool Conflicts(DateTime start, DateTime end, DateTime otherStart, DateTime otherEnd) => start <= otherEnd && otherStart <= end;
    public bool Eligible(Employee employee, CandidateCell cell) =>
        !employee.IsTeamLeader && (!cell.RequiresShiftLeadQualification || employee.IsShiftLeadQualified)
        && !Preferences.Any(p => p.UserId == employee.Id && p.State == PreferenceType.BLOCKED && OverlapsAnchor(cell, p))
        && !Work.Any(w => !w.Replaceable && w.UserId == employee.Id &&
            (Conflicts(cell.Start, cell.End, w.Start, w.End) || (w.Date == cell.Date && w.AnchorType == cell.AnchorType)));
    public static bool OverlapsAnchor(CandidateCell cell, Preference p)
    {
        var interval = ShiftClock.Boundaries(p.Date, p.Type);
        return cell.Start < interval.EndsAt && interval.StartsAt < cell.End;
    }
    public bool Preferred(Employee e, CandidateCell c) => Preferences.Any(p => p.UserId == e.Id && p.State == PreferenceType.PREFERRED && OverlapsAnchor(c, p));
}

public sealed class SchedulingInputs(ApplicationDbContext db)
{
    public async Task<SchedulingProblem> Read(int year, int month, CancellationToken ct = default)
    {
        var first = AvailabilityService.MonthStart(year, month);
        var end = first.AddMonths(1);
        var from = first.AddDays(-2); var through = end.AddDays(2);
        var definitions = await db.Set<ShiftTypeDefinition>().AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(ct);
        var periods = await db.Set<SchedulePeriod>().AsNoTracking().Where(p => p.Year >= from.Year && p.Year <= through.Year).OrderBy(x => x.Id).ToArrayAsync(ct);
        if (periods.Any(p => p.Year == year && p.Month == month && p.Status != ScheduleStatus.DRAFT)) throw new ApiException("PERIOD_PUBLISHED", 409);
        var users = await db.Users.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.IsActive, x.IsShiftLeadQualified, x.FirstName, x.LastName, x.SecurityStamp }).ToArrayAsync(ct);
        var roles = await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id orderby ur.UserId, r.Name select new { ur.UserId, r.Name }).ToArrayAsync(ct);
        var teams = await db.Set<OrganizationalTeam>().AsNoTracking().OrderBy(t => t.Id).ToArrayAsync(ct);
        var employees = users.Where(u => u.IsActive && !teams.Any(t => t.LeaderUserId == u.Id) && roles.Any(r => r.UserId == u.Id && r.Name == "EMPLOYEE"))
            .Select(u => new Employee(u.Id, $"{u.FirstName} {u.LastName}", u.IsShiftLeadQualified)).ToArray();
        var shifts = await db.Set<Shift>().AsNoTracking().Where(s => s.ShiftDate >= from && s.ShiftDate <= through).OrderBy(x => x.Id).ToArrayAsync(ct);
        foreach (var s in shifts)
        {
            var times = ShiftClock.Boundaries(s.ShiftDate, s.ShiftType);
            if (s.StartsAt != times.StartsAt || s.EndsAt != times.EndsAt || !periods.Any(p => p.Id == s.SchedulePeriodId && p.Year == s.ShiftDate.Year && p.Month == s.ShiftDate.Month))
                throw new ApiException("NON_CANONICAL_SHIFT", 409);
        }
        var cells = await db.Set<SchedulingCell>().AsNoTracking().Where(c => c.Date >= from && c.Date <= through).OrderBy(x => x.Id).ToArrayAsync(ct);
        var assignments = await db.Set<ShiftAssignment>().AsNoTracking().Where(a => a.Shift.ShiftDate >= from && a.Shift.ShiftDate <= through)
            .OrderBy(x => x.Id).Select(a => new { a.Id, a.ShiftId, a.UserId, a.IsShiftLead, a.IsLocked, a.SchedulingCellId, a.GeneratedByRunId, HasTasks = a.Tasks.Any() }).ToArrayAsync(ct);
        var submissions = await db.Set<AvailabilitySubmission>().AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(ct);
        var preferences = await db.Set<ShiftPreference>().AsNoTracking().Where(p => p.Shift.ShiftDate >= from && p.Shift.ShiftDate <= through)
            .OrderBy(x => x.Id).Select(p => new { p.Id, p.UserId, p.ShiftId, p.PreferenceType, p.UpdatedAt }).ToArrayAsync(ct);
        // No server-side availability drafts exist. Legacy hard blocks remain binding defensively;
        // preferences count only where a submitted monthly version exists.
        var prefs = preferences.Where(p => p.PreferenceType == PreferenceType.BLOCKED || submissions.Any(s => s.UserId == p.UserId && s.SchedulePeriodId == shifts.Single(x => x.Id == p.ShiftId).SchedulePeriodId))
            .Select(p => { var s = shifts.Single(x => x.Id == p.ShiftId); return new Preference(p.UserId, s.ShiftDate, s.ShiftType, p.PreferenceType); }).ToArray();
        var work = assignments.Select(a => {
            var shift = shifts.Single(s => s.Id == a.ShiftId);
            var cell = cells.SingleOrDefault(c => c.Id == a.SchedulingCellId);
            var definition = definitions.SingleOrDefault(d => d.Id == cell?.ShiftTypeDefinitionId);
            var replaceable = a.GeneratedByRunId.HasValue && !a.IsLocked && !a.IsShiftLead && !a.HasTasks && cell is not null
                && cell.Date >= first && cell.Date < end && definition is { IncludeInAutomaticScheduling: true, RequiredEmployees: > 0 };
            return new Work(a.Id, a.UserId, cell?.ShiftTypeDefinitionId, cell?.Date ?? shift.ShiftDate,
                cell?.StartsAt ?? shift.StartsAt, cell?.EndsAt ?? shift.EndsAt, shift.Id, replaceable, a.IsShiftLead, shift.ShiftType);
        }).ToArray();
        if (work.Any(w => teams.Any(t => t.LeaderUserId == w.UserId))) throw new ApiException("TEAM_LEADER_ASSIGNED", 409);
        var proposed = definitions.Where(d => d.IncludeInAutomaticScheduling && d.RequiredEmployees > 0)
            .SelectMany(d => Enumerable.Range(0, end.DayNumber - first.DayNumber).Select(i => {
                var date = first.AddDays(i); var times = ShiftTimeRanges.Boundaries(date, ShiftTimeRanges.Effective(d));
                return new CandidateCell(d.Id, date, times.StartsAt, times.EndsAt, d.RequiredEmployees, d.Version, d.Period, d.RequiresShiftLeadQualification);
            })).ToArray();
        // Snapshot includes fixed work, configuration, role/account state and submitted versions.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { definitions, teams, periods = periods.Select(p => new { p.Id, p.Year, p.Month, p.Status, p.UpdatedAt }), users, roles,
            shifts = shifts.Select(s => new { s.Id, s.SchedulePeriodId, s.ShiftDate, s.ShiftType, s.StartsAt, s.EndsAt, s.CoordinatorUserId }),
            cells = cells.Select(c => new { c.Id, c.ShiftTypeDefinitionId, c.Date, c.StartsAt, c.EndsAt, c.RequiredEmployees, c.ConfigurationVersion, c.SchedulingRunId }), assignments, submissions = submissions.Select(s => new { s.Id, s.UserId, s.SchedulePeriodId, s.Version, s.LastSubmittedAt }), preferences });
        return new(year, month, Convert.ToHexString(SHA256.HashData(bytes)), proposed, employees, work, prefs);
    }
}
