using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShiftManager.Api;
using ShiftManager.Data;
using ShiftManager.Domain;

namespace ShiftManager.Services;

// Phase 4 must consult this contract inside the same period -> employee lock protocol.
public interface IHardBlockQuery
{
    Task<bool> IsBlocked(Guid userId, Guid shiftId);
    Task<Guid[]> BlockedShifts(Guid userId, DateOnly from, DateOnly through);
}

public sealed class AvailabilityService(ApplicationDbContext db, TimeProvider clock, TeamService teams, AccountService accounts) : IHardBlockQuery
{
    public static DateOnly WeekStart(DateOnly date) => date.AddDays(-(int)date.DayOfWeek);
    public static DateOnly MonthStart(int year, int month)
    {
        // Leave room for boundary weeks and the final night shift's following morning.
        if (year < 2 || year > 9998 || month < 1 || month > 12) throw new ApiException("VALIDATION_ERROR");
        return new DateOnly(year, month, 1);
    }
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    public static WindowResponse Window(AvailabilityWindow? window, DateTime now) => window is null
        ? new("NOT_CONFIGURED", "CLOSED", null, null)
        : new(window.Status == WindowStatus.CLOSED ? "CLOSED" : now < window.OpensAt ? "SCHEDULED"
            : window.ClosesAt is { } end && now >= end ? "EXPIRED" : "OPEN",
            window.Status.ToString(), window.OpensAt, window.ClosesAt);

    private async Task<Guid> Actor(ClaimsPrincipal actor, string role)
    {
        db.ChangeTracker.Clear();
        if (!Guid.TryParse(actor.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) throw new ApiException("UNAUTHENTICATED", 401);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (user is null || !user.IsActive || user.SecurityStamp != actor.FindFirstValue("AspNet.Identity.SecurityStamp"))
            throw new ApiException("UNAUTHENTICATED", 401);
        if (user.MustChangePassword) throw new ApiException("PASSWORD_CHANGE_REQUIRED", 403);
        if (!await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id
                    where ur.UserId == id && r.NormalizedName == role select ur).AnyAsync()) throw new ApiException("FORBIDDEN", 403);
        return id;
    }

    // Global order: month creation lock -> period row -> employee advisory lock.
    // A whole-employee lock safely serializes adjacent-month replacements without week-key races.
    private async Task<SchedulePeriod> LockPeriod(int year, int month, bool create = false)
    {
        MonthStart(year, month);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({71003}, {year * 12 + month})");
        var period = await db.Set<SchedulePeriod>().FromSqlInterpolated($"SELECT * FROM \"SchedulePeriod\" WHERE \"Year\" = {year} AND \"Month\" = {month} FOR UPDATE").SingleOrDefaultAsync();
        if (period is null)
        {
            if (!create) throw new ApiException("PERIOD_NOT_FOUND", 404);
            period = new SchedulePeriod { Year = year, Month = month };
            db.Add(period);
            await db.SaveChangesAsync();
        }
        return period;
    }
    public async Task LockEmployee(Guid userId) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({'a' + userId.ToString("N")}, 0))");

    public async Task<PeriodResponse[]> Periods(Guid userId, ClaimsPrincipal? delegatedActor = null)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        if (delegatedActor is not null) await teams.AuthorizeTarget(delegatedActor, userId);
        var periods = await db.Set<SchedulePeriod>().AsNoTracking().Include(x => x.AvailabilityWindow)
            .OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).ToArrayAsync();
        var submissions = await db.Set<AvailabilitySubmission>().AsNoTracking().Where(x => x.UserId == userId).ToDictionaryAsync(x => x.SchedulePeriodId);
        var now = Now;
        await tx.CommitAsync();
        return periods.Select(p => new PeriodResponse(p.Id, p.Year, p.Month, Window(p.AvailabilityWindow, now),
            submissions.ContainsKey(p.Id), submissions.GetValueOrDefault(p.Id)?.LastSubmittedAt)).ToArray();
    }

    public async Task<AvailabilityResponse> Get(int year, int month, Guid userId, ClaimsPrincipal? delegatedActor = null)
    {
        var first = MonthStart(year, month);
        var end = first.AddMonths(1);
        // A consistent snapshot prevents mixing a new version with old preferences during replacement.
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        if (delegatedActor is not null) await teams.AuthorizeTarget(delegatedActor, userId);
        var period = await db.Set<SchedulePeriod>().AsNoTracking().Include(x => x.AvailabilityWindow)
            .SingleOrDefaultAsync(x => x.Year == year && x.Month == month) ?? throw new ApiException("PERIOD_NOT_FOUND", 404);
        if (!await db.Users.AnyAsync(x => x.Id == userId)) throw new ApiException("USER_NOT_FOUND", 404);
        var submission = await db.Set<AvailabilitySubmission>().AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId && x.SchedulePeriodId == period.Id);
        var selections = submission is null ? [] : await db.Set<ShiftPreference>().AsNoTracking()
            .Where(x => x.UserId == userId && x.Shift.SchedulePeriodId == period.Id).OrderBy(x => x.Shift.ShiftDate).ThenBy(x => x.Shift.ShiftType)
            .Select(x => new SelectionResponse(x.Shift.ShiftDate, x.Shift.ShiftType.ToString(), x.PreferenceType.ToString())).ToArrayAsync();
        var from = WeekStart(first);
        var through = WeekStart(end.AddDays(-1)).AddDays(6);
        var otherBlocks = await SubmittedBlocks(userId).Where(x => x.Shift.SchedulePeriodId != period.Id && x.Shift.ShiftDate >= from && x.Shift.ShiftDate <= through)
            .Select(x => x.Shift.ShiftDate).ToArrayAsync();
        var weeks = Enumerable.Range(0, (through.DayNumber - from.DayNumber) / 7 + 1)
            .Select(i => from.AddDays(i * 7)).Select(w => new WeekResponse(w, otherBlocks.Count(d => WeekStart(d) == w))).ToArray();
        await tx.CommitAsync();
        return new(year, month, userId, Window(period.AvailabilityWindow, Now), submission is not null,
            submission?.LastSubmittedAt, submission?.Version ?? 0, selections, weeks);
    }

    private IQueryable<ShiftPreference> SubmittedBlocks(Guid userId) => db.Set<ShiftPreference>().Where(x => x.UserId == userId
        && x.PreferenceType == PreferenceType.BLOCKED && db.Set<AvailabilitySubmission>().Any(s => s.UserId == userId && s.SchedulePeriodId == x.Shift.SchedulePeriodId));
    // Include legacy rows defensively: an existing hard constraint is never silently ignored.
    public Task<bool> IsBlocked(Guid userId, Guid shiftId) => db.Set<ShiftPreference>()
        .AnyAsync(x => x.UserId == userId && x.ShiftId == shiftId && x.PreferenceType == PreferenceType.BLOCKED);
    public Task<Guid[]> BlockedShifts(Guid userId, DateOnly from, DateOnly through) => db.Set<ShiftPreference>()
        .Where(x => x.UserId == userId && x.PreferenceType == PreferenceType.BLOCKED && x.Shift.ShiftDate >= from && x.Shift.ShiftDate <= through)
        .Select(x => x.ShiftId).ToArrayAsync();

    // Retry only transactions PostgreSQL has aborted; never retry validation/version conflicts.
    private async Task<T> Retry<T>(Func<Task<T>> action)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { return await action(); }
            catch (Exception error) when (attempt < 2 && (error is PostgresException { SqlState: "40001" or "40P01" }
                || error is DbUpdateException { InnerException: PostgresException { SqlState: "40001" or "40P01" } }))
            {
                db.ChangeTracker.Clear();
                await Task.Delay(25 * (attempt + 1));
            }
        }
    }
    public Task<AvailabilityResponse> Submit(int year, int month, SubmitAvailabilityRequest input, ClaimsPrincipal actor, bool update) =>
        Retry(() => SubmitOnce(year, month, input, actor, update));

    public Task<AvailabilityResponse> SubmitDelegated(int year, int month, Guid target, SubmitAvailabilityRequest input, ClaimsPrincipal actor, bool update) =>
        Retry(() => SubmitOnce(year, month, input, actor, update, target));

    private async Task<AvailabilityResponse> SubmitOnce(int year, int month, SubmitAvailabilityRequest input, ClaimsPrincipal actor, bool update, Guid? target = null)
    {
        var first = MonthStart(year, month);
        var end = first.AddMonths(1);
        if (input.Selections is null || input.Selections.Length > 62 || input.Selections.Any(x => x is null || x.Date < first || x.Date >= end
            || x.ShiftType is not ("DAY" or "NIGHT") || x.State is not ("AVAILABLE" or "PREFERRED" or "BLOCKED"))
            || input.Selections.DistinctBy(x => (x.Date, x.ShiftType)).Count() != input.Selections.Length)
            throw new ApiException("VALIDATION_ERROR");
        await using var tx = await db.Database.BeginTransactionAsync();
        var period = await LockPeriod(year, month);
        var periodId = period.Id;
        await accounts.Lock();
        var actorId = target.HasValue ? await teams.Actor(actor) : await Actor(actor, "EMPLOYEE");
        var userId = target ?? actorId;
        Guid? teamId = target.HasValue ? await teams.AuthorizeTarget(actor, userId) : null;
        if (await db.Set<OrganizationalTeam>().AnyAsync(t => t.LeaderUserId == userId)) throw new ApiException("TEAM_LEADER_NOT_SCHEDULED", 409);
        await LockEmployee(userId);
        if (period.Status != ScheduleStatus.DRAFT) throw new ApiException("PERIOD_PUBLISHED", 409);
        var window = await db.Set<AvailabilityWindow>().SingleOrDefaultAsync(x => x.SchedulePeriodId == periodId);
        if (!target.HasValue && Window(window, Now).Status != "OPEN") throw new ApiException("WINDOW_CLOSED", 409);
        var submission = await db.Set<AvailabilitySubmission>().SingleOrDefaultAsync(x => x.UserId == userId && x.SchedulePeriodId == periodId);
        if (update != (submission is not null) || input.ExpectedVersion != (submission?.Version ?? 0)) throw new ApiException("CONFLICT", 409);
        var from = WeekStart(first);
        var through = WeekStart(end.AddDays(-1)).AddDays(6);
        var other = await SubmittedBlocks(userId).Where(x => x.Shift.SchedulePeriodId != periodId && x.Shift.ShiftDate >= from && x.Shift.ShiftDate <= through)
            .Select(x => x.Shift.ShiftDate).ToArrayAsync();
        var overflow = other.Concat(input.Selections.Where(x => x.State == "BLOCKED").Select(x => x.Date))
            .GroupBy(WeekStart).OrderBy(x => x.Key).FirstOrDefault(x => x.Count() > 3);
        if (overflow is not null) throw new ApiException("WEEKLY_BLOCK_LIMIT", 400, overflow.Key, 3);
        var old = await db.Set<ShiftPreference>().Include(x => x.Shift).Where(x => x.UserId == userId && x.Shift.SchedulePeriodId == periodId).ToArrayAsync();
        var previousValues = old.OrderBy(x => x.Shift.ShiftDate).ThenBy(x => x.Shift.ShiftType)
            .Select(x => new { Date = x.Shift.ShiftDate, ShiftType = x.Shift.ShiftType.ToString(), State = x.PreferenceType.ToString() }).ToArray();
        if (teamId.HasValue)
        {
            var audit = new DelegatedAvailabilityAudit
            {
                ActorUserId = actorId, TargetUserId = userId, TeamId = teamId.Value, SchedulePeriodId = periodId,
                OccurredAt = Now, InitialSubmission = !update,
                PreviousValues = JsonSerializer.Serialize(previousValues),
                NewValues = JsonSerializer.Serialize(input.Selections.OrderBy(x => x.Date).ThenBy(x => x.ShiftType))
            };
            db.Add(audit);
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('shiftflow.delegated_audit', {audit.Id.ToString()}, true)");
        }
        db.RemoveRange(old);
        await db.SaveChangesAsync();
        var shifts = await db.Set<Shift>().Where(x => x.SchedulePeriodId == periodId).ToDictionaryAsync(x => (x.ShiftDate, x.ShiftType));
        foreach (var selection in input.Selections.Where(x => x.State != "AVAILABLE"))
        {
            var type = Enum.Parse<ShiftType>(selection.ShiftType);
            if (!shifts.TryGetValue((selection.Date, type), out var shift)) throw new ApiException("CONFLICT", 409);
            // Self-edits reject assignment conflicts; audited delegated edits retain and surface them.
            if (!target.HasValue && selection.State == "BLOCKED" && await db.Set<ShiftAssignment>().AnyAsync(x => x.UserId == userId &&
                (x.SchedulingCellId == null ? x.ShiftId == shift.Id : x.Cell!.StartsAt < shift.EndsAt && x.Cell.EndsAt > shift.StartsAt)))
                throw new ApiException("SHIFT_ALREADY_ASSIGNED", 409);
            db.Add(new ShiftPreference { UserId = userId, ShiftId = shift.Id, PreferenceType = Enum.Parse<PreferenceType>(selection.State), CreatedAt = Now, UpdatedAt = Now });
        }
        if (submission is null)
        {
            submission = new AvailabilitySubmission { UserId = userId, SchedulePeriodId = periodId };
            db.Add(submission);
        }
        submission.Version++;
        submission.LastSubmittedAt = Now;
        // Recheck the clock immediately before committing the atomic replacement.
        if (!target.HasValue && Window(window, Now).Status != "OPEN") throw new ApiException("WINDOW_CLOSED", 409);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return await Get(year, month, userId, target.HasValue ? actor : null);
    }

    public Task<WindowSummaryResponse> Configure(int year, int month, WindowRequest input, ClaimsPrincipal actor) =>
        Retry(() => ConfigureOnce(year, month, input, actor));

    private async Task<WindowSummaryResponse> ConfigureOnce(int year, int month, WindowRequest input, ClaimsPrincipal actor)
    {
        if (input.OpensAt is null || input.ClosesAt is null || input.ClosesAt <= input.OpensAt) throw new ApiException("INVALID_WINDOW_TIME");
        await using var tx = await db.Database.BeginTransactionAsync();
        var period = await LockPeriod(year, month, true);
        var periodId = period.Id;
        var actorId = await Actor(actor, "ADMIN");
        if (period.Status != ScheduleStatus.DRAFT) throw new ApiException("PERIOD_PUBLISHED", 409);
        var window = await db.Set<AvailabilityWindow>().SingleOrDefaultAsync(x => x.SchedulePeriodId == periodId);
        if (window is null)
        {
            window = new AvailabilityWindow { SchedulePeriodId = periodId, Status = WindowStatus.CLOSED, OpenedByUserId = actorId, ClosedByUserId = actorId };
            db.Add(window);
        }
        // Configuration never opens/reopens a window implicitly.
        window.OpensAt = input.OpensAt.Value.UtcDateTime;
        window.ClosesAt = input.ClosesAt.Value.UtcDateTime;
        await EnsureShifts(period);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return await Summary(year, month);
    }

    private async Task EnsureShifts(SchedulePeriod period)
    {
        var first = MonthStart(period.Year, period.Month);
        var existing = await db.Set<Shift>().Where(x => x.SchedulePeriodId == period.Id).ToDictionaryAsync(x => (x.ShiftDate, x.ShiftType));
        for (var day = first; day < first.AddMonths(1); day = day.AddDays(1))
            foreach (var type in Enum.GetValues<ShiftType>())
            {
                var (start, end) = ShiftClock.Boundaries(day, type);
                if (existing.TryGetValue((day, type), out var shift))
                {
                    if (shift.StartsAt != start || shift.EndsAt != end) throw new ApiException("NON_CANONICAL_SHIFT", 409);
                }
                else db.Add(new Shift { SchedulePeriodId = period.Id, ShiftDate = day, ShiftType = type, StartsAt = start, EndsAt = end });
            }
    }

    public Task<WindowSummaryResponse> SetState(int year, int month, bool open, ClaimsPrincipal actor) =>
        Retry(() => SetStateOnce(year, month, open, actor));

    private async Task<WindowSummaryResponse> SetStateOnce(int year, int month, bool open, ClaimsPrincipal actor)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var period = await LockPeriod(year, month);
        var periodId = period.Id;
        var actorId = await Actor(actor, "ADMIN");
        if (period.Status != ScheduleStatus.DRAFT) throw new ApiException("PERIOD_PUBLISHED", 409);
        var window = await db.Set<AvailabilityWindow>().SingleOrDefaultAsync(x => x.SchedulePeriodId == periodId) ?? throw new ApiException("WINDOW_NOT_CONFIGURED", 409);
        if (open && (window.ClosesAt is null || window.ClosesAt <= Now || window.ClosesAt <= window.OpensAt)) throw new ApiException("INVALID_WINDOW_TIME");
        window.Status = open ? WindowStatus.OPEN : WindowStatus.CLOSED;
        if (open) { window.OpenedByUserId = actorId; window.ClosedByUserId = null; }
        else window.ClosedByUserId = actorId;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return await Summary(year, month);
    }

    public async Task<WindowSummaryResponse> Summary(int year, int month)
    {
        MonthStart(year, month);
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        var period = await db.Set<SchedulePeriod>().AsNoTracking().Include(x => x.AvailabilityWindow).SingleOrDefaultAsync(x => x.Year == year && x.Month == month);
        var employees = await (from u in db.Users.AsNoTracking()
            where u.IsActive && !db.Set<OrganizationalTeam>().Any(t => t.LeaderUserId == u.Id) && (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id
                where ur.UserId == u.Id && r.NormalizedName == "EMPLOYEE" select ur).Any()
            orderby u.LastName, u.FirstName, u.Id
            select new { u.Id, u.FirstName, u.LastName }).ToArrayAsync();
        var periodId = period?.Id;
        var submissions = await db.Set<AvailabilitySubmission>().AsNoTracking().Where(x => x.SchedulePeriodId == periodId).ToDictionaryAsync(x => x.UserId);
        var rows = employees.Select(e => new EmployeeSubmissionResponse(e.Id, e.FirstName, e.LastName,
            submissions.ContainsKey(e.Id), submissions.GetValueOrDefault(e.Id)?.LastSubmittedAt)).ToArray();
        await tx.CommitAsync();
        return new(year, month, Window(period?.AvailabilityWindow, Now), rows.Length, rows.Count(x => x.Submitted), rows);
    }
}
