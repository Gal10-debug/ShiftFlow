using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Api;
using ShiftManager.Data;
using ShiftManager.Domain;
namespace ShiftManager.Services;

public sealed partial class AbsenceService(ApplicationDbContext db, AccountService accounts, TeamService teams, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private async Task<Guid> Lock(ClaimsPrincipal actor)
    {
        // Same order as scheduling writers. The account lock also serializes team
        // membership/leadership changes and availability mutations with decisions.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(71004)");
        // Availability writers take period locks before the account lock. Acquire
        // existing periods in the same order before a decision can remove draft work.
        foreach (var period in await db.Set<SchedulePeriod>().AsNoTracking().OrderBy(p => p.Year).ThenBy(p => p.Month).ToArrayAsync())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({71003}, {period.Year * 12 + period.Month})");
            await db.Set<SchedulePeriod>().FromSqlInterpolated($"SELECT * FROM \"SchedulePeriod\" WHERE \"Id\" = {period.Id} FOR UPDATE").LoadAsync();
        }
        await accounts.Lock();
        return await teams.Actor(actor);
    }
    private Task<bool> HasRole(Guid user, string role) => (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id
        where ur.UserId == user && r.NormalizedName == role select ur).AnyAsync();
    private async Task RequireEmployee(Guid user)
    {
        if (!await HasRole(user, "EMPLOYEE")) throw new ApiException("FORBIDDEN", 403);
    }
    private async Task AuthorizeApprover(Guid actor, Guid target)
    {
        if (await HasRole(actor, "ADMIN")) return;
        if (!await (from m in db.Set<TeamMembership>() join t in db.Set<OrganizationalTeam>() on m.TeamId equals t.Id
            where m.UserId == target && (t.LeaderUserId == actor || t.DeputyUserId == actor) select m).AnyAsync()) throw new ApiException("FORBIDDEN", 403);
    }
    private async Task<AbsenceRequest> Request(Guid id) => await db.Set<AbsenceRequest>().Include(r => r.Weekdays).SingleOrDefaultAsync(r => r.Id == id)
        ?? throw new ApiException("NOT_FOUND", 404);
    private static AbsencePattern Pattern(AbsenceRequest r) => new(r.Reason, r.StartDate, r.EndDate, r.Recurring,
        r.Weekdays.Select(w => w.Day).Order().ToArray(), r.FullDay, r.StartTime, r.EndTime, r.Explanation);
    private static void Validate(AbsencePattern p)
    {
        if (p.Reason is not ("STUDIES" or "RESERVE" or "VACATION" or "OTHER") || p.Explanation?.Length > 2000
            || p.StartDate < new DateOnly(2, 1, 1) || p.StartDate > new DateOnly(9998, 12, 30)
            || p.EndDate < p.StartDate || p.EndDate > new DateOnly(9998, 12, 30) || !p.Recurring && p.EndDate is null
            || p.Weekdays is null || p.Weekdays.Distinct().Count() != p.Weekdays.Length || p.Weekdays.Any(d => d is < 0 or > 6)
            || (p.Recurring ? p.Weekdays.Length == 0 : p.Weekdays.Length != 0)
            || (p.FullDay ? p.StartTime is not null || p.EndTime is not null : p.StartTime is null || p.EndTime is null || p.StartTime == p.EndTime))
            throw new ApiException("INVALID_ABSENCE");
    }
    private AbsenceRequest Create(Guid user, AbsencePattern p, string kind = "CREATE", AbsenceRequest? parent = null) => new()
    {
        UserId = user, Kind = kind, Reason = p.Reason, Explanation = p.Explanation?.Trim(), StartDate = p.StartDate, EndDate = p.EndDate,
        Recurring = p.Recurring, FullDay = p.FullDay, StartTime = p.StartTime, EndTime = p.EndTime,
        Weekdays = p.Weekdays.Select(d => new AbsenceWeekday { Day = d }).ToList(),
        ReplacesId = parent?.Id, ReplacesVersion = parent?.Version, CreatedAt = Now, UpdatedAt = Now
    };
    public async Task<AbsenceResponse[]> List(ClaimsPrincipal actor, bool manage = false, Guid? teamId = null, Guid? userId = null,
        string? status = null, DateOnly? from = null, DateOnly? through = null)
    {
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead) : null;
        var id = await teams.Actor(actor);
        var query = db.Set<AbsenceRequest>().AsNoTracking().Include(r => r.Weekdays).AsQueryable();
        if (!manage) { await RequireEmployee(id); query = query.Where(r => r.UserId == id); }
        else if (!await HasRole(id, "ADMIN"))
        {
            var scope = await db.Set<OrganizationalTeam>().Where(t => t.LeaderUserId == id || t.DeputyUserId == id).Select(t => t.Id).ToArrayAsync();
            if (scope.Length == 0) throw new ApiException("FORBIDDEN", 403);
            query = query.Where(r => db.Set<TeamMembership>().Any(m => m.UserId == r.UserId && scope.Contains(m.TeamId)));
        }
        if (teamId.HasValue) query = query.Where(r => db.Set<TeamMembership>().Any(m => m.UserId == r.UserId && m.TeamId == teamId));
        if (userId.HasValue) query = query.Where(r => r.UserId == userId);
        if (status is not null)
        {
            if (status is not ("PENDING" or "APPROVED" or "REJECTED" or "CANCELLED")) throw new ApiException("VALIDATION_ERROR");
            query = query.Where(r => r.Status == status);
        }
        if (from.HasValue) query = query.Where(r => r.EndDate == null || r.EndDate >= from);
        if (through.HasValue) query = query.Where(r => r.StartDate <= through);
        if (from > through) throw new ApiException("VALIDATION_ERROR");
        var requests = await query.OrderByDescending(r => r.CreatedAt).ThenBy(r => r.Id).ToArrayAsync();
        var result = new List<AbsenceResponse>();
        foreach (var r in requests) result.Add(await Response(r, await HasRole(id, "ADMIN")));
        if (tx is not null) await tx.CommitAsync(); return result.ToArray();
    }
    private async Task<AbsenceResponse> Response(AbsenceRequest r, bool admin = false)
    {
        var name = await db.Users.Where(u => u.Id == r.UserId).Select(u => u.FirstName + " " + u.LastName).SingleAsync();
        var team = await (from m in db.Set<TeamMembership>() join t in db.Set<OrganizationalTeam>() on m.TeamId equals t.Id
            where m.UserId == r.UserId select new { t.Id, t.Name }).SingleOrDefaultAsync();
        var decision = await (from d in db.Set<AbsenceDecision>() join u in db.Users on d.ActorUserId equals u.Id
            where d.AbsenceRequestId == r.Id select new AbsenceDecisionResponse(d.ActorUserId, u.FirstName + " " + u.LastName, d.Decision, d.OccurredAt, d.RejectionReason)).SingleOrDefaultAsync();
        var conflicts = await (from c in db.Set<AbsenceConflict>() join p in db.Set<SchedulePeriod>() on c.SchedulePeriodId equals p.Id
            join pub in db.Set<SchedulePublication>() on c.PublicationId equals pub.Id
            where c.AbsenceRequestId == r.Id && c.ResolvedAt == null && (admin || PublicationAccess.Visible(db).Any(p => p.Id == c.PublicationId)) orderby c.Date, c.Id
            select new AbsenceConflictItem(c.AssignmentId, p.Id, p.Year, p.Month, c.Date, c.StartsAt, c.EndsAt, c.ShiftName,
                "PUBLISHED_CONFLICT", c.PublicationId, pub.VersionNumber, false, null)).ToArrayAsync();
        return new(r.Id, r.UserId, name, team?.Id, team?.Name, r.Kind, r.Status, Pattern(r), r.ReplacesId, r.Version,
            r.CreatedAt, r.UpdatedAt, decision, conflicts);
    }
    // Join a caller-owned transaction for atomic development scenario batches.
    // Ordinary HTTP calls still own and commit their individual transactions.
    public async Task<AbsenceResponse> Submit(AbsencePattern p, ClaimsPrincipal actor)
    {
        Validate(p);
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync() : null; var id = await Lock(actor); await RequireEmployee(id);
        var r = Create(id, p); db.Add(r); await db.SaveChangesAsync(); var result = await Response(r, await HasRole(id, "ADMIN")); if (tx is not null) await tx.CommitAsync(); return result;
    }
    public async Task<AbsenceResponse> Change(Guid requestId, AbsenceChange input, ClaimsPrincipal actor)
    {
        if (input.Kind is not ("MODIFY" or "CANCEL")) throw new ApiException("INVALID_ABSENCE");
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync() : null; var id = await Lock(actor); await RequireEmployee(id);
        var parent = await Request(requestId);
        if (parent.UserId != id) throw new ApiException("FORBIDDEN", 403);
        if (parent.Status != "APPROVED" || parent.Kind == "CANCEL" || parent.Version != input.ExpectedVersion) throw new ApiException("CONFLICT", 409);
        var p = input.Kind == "CANCEL" ? Pattern(parent) : input.Pattern ?? throw new ApiException("INVALID_ABSENCE"); Validate(p);
        if (await db.Set<AbsenceRequest>().AnyAsync(r => r.ReplacesId == parent.Id && r.Status == "PENDING")) throw new ApiException("ABSENCE_CHANGE_PENDING", 409);
        var r = Create(id, p, input.Kind, parent); db.Add(r); await db.SaveChangesAsync(); var result = await Response(r, await HasRole(id, "ADMIN")); if (tx is not null) await tx.CommitAsync(); return result;
    }
    public async Task<AbsenceResponse> CancelPending(Guid requestId, AbsenceVersion input, ClaimsPrincipal actor)
    {
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync() : null; var id = await Lock(actor); await RequireEmployee(id);
        var r = await Request(requestId);
        if (r.UserId != id) throw new ApiException("FORBIDDEN", 403);
        if (r.Status != "PENDING" || r.Version != input.ExpectedVersion) throw new ApiException("CONFLICT", 409);
        r.Status = "CANCELLED"; r.Version++; r.UpdatedAt = Now;
        db.Add(new AbsenceDecision { AbsenceRequestId = r.Id, ActorUserId = id, Decision = "CANCELLED", OccurredAt = Now });
        await db.SaveChangesAsync(); var result = await Response(r, await HasRole(id, "ADMIN")); if (tx is not null) await tx.CommitAsync(); return result;
    }
    public async Task<AbsenceResponse> Decide(Guid requestId, AbsenceDecisionRequest input, ClaimsPrincipal actor)
    {
        if (input.Decision is not ("APPROVED" or "REJECTED") || input.RejectionReason?.Length > 2000
            || input.Decision == "REJECTED" && string.IsNullOrWhiteSpace(input.RejectionReason)) throw new ApiException("INVALID_ABSENCE_DECISION");
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync() : null; var id = await Lock(actor);
        var r = await Request(requestId); await AuthorizeApprover(id, r.UserId);
        // Team authority does not permit deciding the actor's own proposal.
        if (r.UserId == id) throw new ApiException("SELF_ABSENCE_DECISION", 403);
        if (r.Status != "PENDING" || r.Version != input.ExpectedVersion) throw new ApiException("CONFLICT", 409);
        if (input.Decision == "APPROVED")
        {
            var report = await FindConflicts(r);
            await RequireVisibleConflicts(report, id);
            if (input.ConflictToken != report.Token) throw new ApiException("STALE_ABSENCE_CONFLICTS", 409);
            if (report.Items.Any(c => c.RemovalBlock is not null)) throw new ApiException("ABSENCE_CONFLICT_REQUIRES_ADMIN", 409);
            if (report.Items.Any(c => c.Removable) && !input.ConfirmDraftRemovals) throw new ApiException("ABSENCE_DRAFT_CONFIRMATION", 409);
            await ApplyConflicts(r, report, id);
            if (r.ReplacesId.HasValue)
            {
                var previous = await Request(r.ReplacesId.Value);
                if (previous.Status != "APPROVED" || previous.Version != r.ReplacesVersion) throw new ApiException("CONFLICT", 409);
                previous.Status = "CANCELLED"; previous.Version++; previous.UpdatedAt = Now;
                // Supersession is itself audited by the immutable child decision.
            }
        }
        r.Status = input.Decision; r.Version++; r.UpdatedAt = Now;
        db.Add(new AbsenceDecision { AbsenceRequestId = r.Id, ActorUserId = id, Decision = input.Decision,
            RejectionReason = input.Decision == "REJECTED" ? input.RejectionReason!.Trim() : null, OccurredAt = Now });
        await db.SaveChangesAsync(); var result = await Response(r, await HasRole(id, "ADMIN")); if (tx is not null) await tx.CommitAsync(); return result;
    }
    private async Task RequireVisibleConflicts(AbsenceConflictReport report, Guid actor)
    {
        if (await HasRole(actor, "ADMIN")) return;
        var ids = report.Items.Where(c => c.PublicationId.HasValue).Select(c => c.PublicationId!.Value).Distinct().ToArray();
        if (ids.Length != await PublicationAccess.Visible(db).CountAsync(p => ids.Contains(p.Id)))
            throw new ApiException("ABSENCE_CONFLICT_REQUIRES_ADMIN", 409);
    }
    public async Task<AbsenceConflictReport> Conflicts(Guid requestId, ClaimsPrincipal actor)
    {
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead) : null;
        var id = await teams.Actor(actor); var r = await Request(requestId); await AuthorizeApprover(id, r.UserId);
        var report = await FindConflicts(r); await RequireVisibleConflicts(report, id); if (tx is not null) await tx.CommitAsync(); return report;
    }
}
