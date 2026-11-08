using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Api;
using ShiftManager.Data;
using ShiftManager.Domain;

namespace ShiftManager.Services;

/// <summary>Authorized reporting entry points for HTTP and future approved application tools.</summary>
public sealed class SchedulingAnalyticsService(ApplicationDbContext db, TeamService teams)
{
    private sealed record PublishedMonth(AnalyticsPublication Publication, ScheduleSnapshot Snapshot);

    private static int[] Months(int? month) => month.HasValue ? [month.Value] : Enumerable.Range(1, 12).ToArray();
    private static void Validate(int year, int? month, int? version)
    {
        AvailabilityService.MonthStart(year, month ?? 1);
        if (version is <= 0 || version.HasValue && !month.HasValue) throw new ApiException("VALIDATION_ERROR");
    }

    // One bounded database read for all required months. Latest must itself be visible;
    // selecting the latest visible row would incorrectly fall back past a hidden version.
    private async Task<Dictionary<(int Year, int Month), PublishedMonth>> Read(int year, int? month, int? version, bool comparison)
    {
        var first = AvailabilityService.MonthStart(year, month ?? 1);
        var previous = first.AddMonths(-1);
        comparison = comparison && previous.Year >= 2;
        var query = from pub in PublicationAccess.Visible(db)
                    join period in db.Set<SchedulePeriod>() on pub.SchedulePeriodId equals period.Id
                    where (period.Year == year && (!month.HasValue || period.Month == month)
                        || comparison && period.Year == previous.Year && period.Month == previous.Month)
                        && (version.HasValue && period.Year == year && period.Month == month
                            ? pub.VersionNumber == version : PublicationAccess.Latest(db).Any(p => p.Id == pub.Id))
                    select new { period.Year, period.Month, pub.Id, pub.VersionNumber, pub.PublicationType, pub.Snapshot };
        var rows = await query.OrderBy(r => r.Year).ThenBy(r => r.Month).ToArrayAsync();
        var result = new Dictionary<(int, int), PublishedMonth>();
        foreach (var row in rows)
        {
            var snapshot = JsonSerializer.Deserialize<ScheduleSnapshot>(row.Snapshot);
            if (snapshot is null) continue;
            if (snapshot.Year != row.Year || snapshot.Month != row.Month) throw new ApiException("INTERNAL_ERROR", 500);
            result.Add((row.Year, row.Month), new(new(row.Id, row.VersionNumber, row.PublicationType), snapshot));
        }
        if (version.HasValue && !result.ContainsKey((year, month!.Value))) throw new ApiException("NOT_FOUND", 404);
        return result;
    }

    public async Task<PersonalAnalytics> Personal(int year, int? month, ClaimsPrincipal actor, int? version = null)
    {
        Validate(year, month, version);
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        var id = await teams.SchedulingEmployee(actor);
        var data = await Read(year, month, version, month.HasValue);
        AnalyticsMonth Map(int y, int m) => data.TryGetValue((y, m), out var p)
            ? new(y, m, p.Publication, SchedulingAnalyticsCalculator.Totals(PlannedWorkHoursCalculator.Calculate(p.Snapshot, id, y, m)))
            : new(y, m, null, SchedulingAnalyticsCalculator.Empty);
        var months = Months(month).Select(m => Map(year, m)).ToArray();
        AnalyticsComparison? comparison = null;
        if (month.HasValue)
        {
            var prior = new DateOnly(year, month.Value, 1).AddMonths(-1);
            var previous = Map(prior.Year, prior.Month);
            var comparable = months[0].Publication is not null && previous.Publication is not null;
            comparison = new(previous, comparable ? months[0].Totals.TotalAssignedShifts - previous.Totals.TotalAssignedShifts : null,
                comparable ? months[0].Totals.KnownPlannedMinutes - previous.Totals.KnownPlannedMinutes : null,
                comparable ? months[0].Totals.IsComplete && previous.Totals.IsComplete : null);
        }
        var response = new PersonalAnalytics(year, month, SchedulingAnalyticsCalculator.Sum(months.Select(m => m.Totals)), months, comparison);
        await tx.CommitAsync(); return response;
    }

    public async Task<AdminAnalytics> Administrative(int year, int? month, ClaimsPrincipal actor,
        Guid? teamId = null, Guid? employeeId = null, int? version = null)
    {
        Validate(year, month, version);
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        await teams.Actor(actor, true);
        var teamRows = await db.Set<OrganizationalTeam>().AsNoTracking().OrderBy(t => t.Slot).ToArrayAsync();
        if (teamId.HasValue && !teamRows.Any(t => t.Id == teamId)) throw new ApiException("NOT_FOUND", 404);
        var members = await db.Set<TeamMembership>().AsNoTracking().ToDictionaryAsync(m => m.UserId, m => m.TeamId);
        var users = await db.Users.AsNoTracking().Select(u => new { u.Id, Name = u.FirstName + " " + u.LastName,
            Eligible = u.IsActive && db.UserRoles.Any(ur => ur.UserId == u.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.NormalizedName == "EMPLOYEE"))
                && !db.UserRoles.Any(ur => ur.UserId == u.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.NormalizedName == "ADMIN"))
                && !db.Set<OrganizationalTeam>().Any(t => t.LeaderUserId == u.Id) }).ToArrayAsync();
        if (employeeId.HasValue && !users.Any(u => u.Id == employeeId)) throw new ApiException("NOT_FOUND", 404);
        bool Included(Guid id) => (!teamId.HasValue || members.GetValueOrDefault(id) == teamId)
            && (!employeeId.HasValue || id == employeeId);
        if (employeeId.HasValue && !Included(employeeId.Value)) throw new ApiException("VALIDATION_ERROR");
        var data = await Read(year, month, version, false);
        var byMonth = data.ToDictionary(p => p.Key, p => SchedulingAnalyticsCalculator.Employees(p.Value.Snapshot));
        var ids = users.Where(u => u.Eligible).Select(u => u.Id).Union(byMonth.Values.SelectMany(d => d.Keys)).Where(Included).Order().ToArray();
        var names = users.ToDictionary(u => u.Id, u => u.Name);
        var employees = ids.Select(id => new EmployeeWorkload(id, names.GetValueOrDefault(id) ?? "עובד היסטורי",
            members.TryGetValue(id, out var team) ? team : null,
            SchedulingAnalyticsCalculator.Sum(byMonth.Values.Select(d => d.GetValueOrDefault(id) ?? SchedulingAnalyticsCalculator.Empty))))
            .OrderBy(e => e.Name, StringComparer.Ordinal).ThenBy(e => e.EmployeeId).ToArray();
        var organization = !teamId.HasValue && !employeeId.HasValue;
        var months = Months(month).Select(m => data.TryGetValue((year, m), out var p)
            ? new AnalyticsMonth(year, m, p.Publication, SchedulingAnalyticsCalculator.Sum(byMonth[(year, m)].Where(e => Included(e.Key)).Select(e => e.Value)),
                organization ? SchedulingAnalyticsCalculator.Coverage(p.Snapshot) : null)
            : new(year, m, null, SchedulingAnalyticsCalculator.Empty)).ToArray();
        var summaries = employees.GroupBy(e => e.TeamId).OrderBy(g => g.Key).Select(g => new TeamWorkload(g.Key,
            teamRows.FirstOrDefault(t => t.Id == g.Key)?.Name ?? "ללא צוות", g.Count(e => e.Totals.TotalAssignedShifts > 0),
            SchedulingAnalyticsCalculator.Sum(g.Select(e => e.Totals)))).ToArray();
        var response = new AdminAnalytics(year, month, teamId, employeeId, SchedulingAnalyticsCalculator.Sum(months.Select(m => m.Totals)),
            months, SchedulingAnalyticsCalculator.SumCoverage(months.Select(m => m.Coverage)),
            SchedulingAnalyticsCalculator.Distribution(employees), employees, summaries);
        await tx.CommitAsync(); return response;
    }
}
