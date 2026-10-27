using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Api;
using ShiftManager.Data;
using ShiftManager.Domain;
namespace ShiftManager.Services;

// Uses the account lock so membership, role changes and delegated writes cannot race.
public sealed class TeamService(ApplicationDbContext db, AccountService accounts)
{
    public async Task<Guid> Actor(ClaimsPrincipal actor, bool admin = false)
    {
        if (!Guid.TryParse(actor.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) throw new ApiException("UNAUTHENTICATED", 401);
        var u = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (u is null || !u.IsActive || u.SecurityStamp != actor.FindFirstValue("AspNet.Identity.SecurityStamp")) throw new ApiException("UNAUTHENTICATED", 401);
        if (u.MustChangePassword) throw new ApiException("PASSWORD_CHANGE_REQUIRED", 403);
        if (admin && !await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id where ur.UserId == id && r.NormalizedName == "ADMIN" select ur).AnyAsync()) throw new ApiException("FORBIDDEN", 403);
        return id;
    }
    public async Task<Guid> AuthorizeTarget(ClaimsPrincipal actor, Guid target)
    {
        var id = await Actor(actor);
        var team = await (from m in db.Set<TeamMembership>() join t in db.Set<OrganizationalTeam>() on m.TeamId equals t.Id
            where m.UserId == target && (t.LeaderUserId == id || t.DeputyUserId == id) select t.Id).SingleOrDefaultAsync();
        if (team == Guid.Empty) throw new ApiException("FORBIDDEN", 403);
        // Leaders are not scheduling participants, including when the actor targets themselves.
        if (await db.Set<OrganizationalTeam>().AnyAsync(t => t.LeaderUserId == target)) throw new ApiException("TEAM_LEADER_NOT_SCHEDULED", 409);
        if (!await (from u in db.Users join ur in db.UserRoles on u.Id equals ur.UserId join r in db.Roles on ur.RoleId equals r.Id
            where u.Id == target && u.IsActive && r.NormalizedName == "EMPLOYEE" select u).AnyAsync()) throw new ApiException("FORBIDDEN", 403);
        return team;
    }
    public async Task<TeamResponse[]> List(ClaimsPrincipal actor, bool admin)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        var id = await Actor(actor, admin);
        var teams = await db.Set<OrganizationalTeam>().AsNoTracking().Where(t => admin || t.LeaderUserId == id || t.DeputyUserId == id).OrderBy(t => t.Slot).ToArrayAsync();
        if (!admin && teams.Length == 0) throw new ApiException("FORBIDDEN", 403);
        var teamIds = teams.Select(t => t.Id).ToArray();
        var members = await (from m in db.Set<TeamMembership>() join u in db.Users on m.UserId equals u.Id
            where teamIds.Contains(m.TeamId) orderby u.LastName, u.FirstName select new { m.TeamId, Member = new TeamMemberResponse(u.Id, u.FirstName, u.LastName, u.IsActive, (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id where ur.UserId == u.Id && r.NormalizedName == "EMPLOYEE" select ur).Any()) }).ToArrayAsync();
        await tx.CommitAsync();
        return teams.Select(t => new TeamResponse(t.Id, t.Name, t.LeaderUserId, t.DeputyUserId, t.Version, members.Where(m => m.TeamId == t.Id).Select(m => m.Member).ToArray())).ToArray();
    }
    public async Task Update(Guid id, UpdateTeamRequest input, ClaimsPrincipal actor)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await accounts.Lock(); await Actor(actor, true);
        var t = await db.Set<OrganizationalTeam>().SingleOrDefaultAsync(t => t.Id == id) ?? throw new ApiException("NOT_FOUND", 404);
        if (t.Version != input.ExpectedVersion) throw new ApiException("CONFLICT", 409);
        if (input.LeaderUserId.HasValue && input.LeaderUserId == input.DeputyUserId) throw new ApiException("INVALID_TEAM_LEADERSHIP");
        foreach (var userId in new[] { input.LeaderUserId, input.DeputyUserId }.Where(x => x.HasValue))
            if (!await (from m in db.Set<TeamMembership>() join u in db.Users on m.UserId equals u.Id
                where m.TeamId == id && m.UserId == userId && u.IsActive select m).AnyAsync()) throw new ApiException("INVALID_TEAM_LEADERSHIP");
        if (input.DeputyUserId.HasValue && !await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id
            where ur.UserId == input.DeputyUserId && r.NormalizedName == "EMPLOYEE" select ur).AnyAsync()) throw new ApiException("DEPUTY_REQUIRES_EMPLOYEE");
        t.Name = input.Name.Trim(); t.LeaderUserId = input.LeaderUserId; t.DeputyUserId = input.DeputyUserId; t.Version++;
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
    public async Task Membership(Guid userId, Guid? teamId, ClaimsPrincipal actor)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await accounts.Lock(); await Actor(actor, true);
        if (!await db.Users.AnyAsync(u => u.Id == userId)) throw new ApiException("USER_NOT_FOUND", 404);
        if (teamId.HasValue && !await db.Set<OrganizationalTeam>().AnyAsync(t => t.Id == teamId)) throw new ApiException("NOT_FOUND", 404);
        var old = await db.Set<TeamMembership>().SingleOrDefaultAsync(m => m.UserId == userId);
        if (old?.TeamId == teamId) { await tx.CommitAsync(); return; }
        foreach (var t in await db.Set<OrganizationalTeam>().Where(t => t.LeaderUserId == userId || t.DeputyUserId == userId || t.Id == teamId || (old != null && t.Id == old.TeamId)).ToArrayAsync())
        {
            if (t.LeaderUserId == userId) t.LeaderUserId = null;
            if (t.DeputyUserId == userId) t.DeputyUserId = null;
            t.Version++;
        }
        await db.SaveChangesAsync();
        if (old is not null) { db.Remove(old); await db.SaveChangesAsync(); }
        if (teamId.HasValue) db.Add(new TeamMembership { UserId = userId, TeamId = teamId.Value });
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
}
