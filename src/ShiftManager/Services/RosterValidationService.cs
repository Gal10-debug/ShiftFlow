using Microsoft.EntityFrameworkCore;
using ShiftManager.Api;
using ShiftManager.Data;
using ShiftManager.Domain;
namespace ShiftManager.Services;
public sealed record RosterConflict(Guid AssignmentId, Guid UserId, Guid SchedulePeriodId, DateOnly Date, string Code);

// Reusable guard for future publication: call under the scheduling/account/period locks.
public sealed class RosterValidationService(ApplicationDbContext db)
{
    public async Task<RosterConflict[]> Conflicts(Guid? periodId = null)
    {
        var leaders = await db.Set<OrganizationalTeam>().Where(t => t.LeaderUserId != null).Select(t => t.LeaderUserId!.Value).ToArrayAsync();
        var assignments = await db.Set<ShiftAssignment>().AsNoTracking().Where(a => periodId == null || a.Shift.SchedulePeriodId == periodId)
            .Select(a => new { a.Id, a.UserId, a.ShiftId, a.Shift.SchedulePeriodId, a.Shift.ShiftDate,
                Start = a.SchedulingCellId == null ? a.Shift.StartsAt : a.Cell!.StartsAt,
                End = a.SchedulingCellId == null ? a.Shift.EndsAt : a.Cell!.EndsAt }).ToArrayAsync();
        var users = assignments.Select(a => a.UserId).Distinct().ToArray();
        var blocks = await db.Set<ShiftPreference>().AsNoTracking().Where(p => users.Contains(p.UserId) && p.PreferenceType == PreferenceType.BLOCKED)
            .Select(p => new { p.UserId, p.ShiftId, p.Shift.StartsAt, p.Shift.EndsAt }).ToArrayAsync();
        return assignments.SelectMany(a => {
            var result = new List<RosterConflict>();
            if (leaders.Contains(a.UserId)) result.Add(new(a.Id, a.UserId, a.SchedulePeriodId, a.ShiftDate, "TEAM_LEADER_ASSIGNED"));
            if (blocks.Any(b => b.UserId == a.UserId && b.StartsAt < a.End && b.EndsAt > a.Start)) result.Add(new(a.Id, a.UserId, a.SchedulePeriodId, a.ShiftDate, "HARD_BLOCK_CONFLICT"));
            return result;
        }).OrderBy(c => c.Date).ThenBy(c => c.AssignmentId).ToArray();
    }
    public async Task ValidateForPublication(Guid periodId)
    {
        if ((await Conflicts(periodId)).Length > 0) throw new ApiException("ROSTER_CONFLICT", 409);
    }
}
