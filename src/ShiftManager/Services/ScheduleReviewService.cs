using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Api;
using ShiftManager.Data;
using ShiftManager.Domain;
using ShiftManager.Services.AutomaticScheduling;
namespace ShiftManager.Services;

public sealed class ScheduleReviewService(ApplicationDbContext db, AccountService accounts, TeamService teams,
    AvailabilityService availability, SchedulingInputs inputs, TimeProvider clock)
{
    public static bool Editable(ScheduleStatus status) => status is ScheduleStatus.DRAFT or ScheduleStatus.UNDER_CORRECTION;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));

    // Separate input revision: changing availability/eligibility/configuration requires explicit review.
    public static async Task<string> InputRevision(ApplicationDbContext db)
    {
        var submissions = await db.Set<AvailabilitySubmission>().AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Version }).ToArrayAsync();
        var preferences = await db.Set<ShiftPreference>().AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.UserId, x.ShiftId, x.PreferenceType, x.UpdatedAt }).ToArrayAsync();
        var users = await db.Users.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.IsActive, x.IsShiftLeadQualified }).ToArrayAsync();
        var roles = await db.UserRoles.AsNoTracking().OrderBy(x => x.UserId).ThenBy(x => x.RoleId).Select(x => new { x.UserId, x.RoleId }).ToArrayAsync();
        var leadership = await db.Set<OrganizationalTeam>().AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.LeaderUserId }).ToArrayAsync();
        var definitions = await db.Set<ShiftTypeDefinition>().AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Version }).ToArrayAsync();
        return Hash(new { submissions, preferences, users, roles, leadership, definitions });
    }
    private async Task<Guid> Lock(int year, int month, ClaimsPrincipal actor, bool admin = true)
    {
        var first = AvailabilityService.MonthStart(year, month);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(71004)");
        foreach (var day in new[] { first.AddMonths(-1), first, first.AddMonths(1) })
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({71003}, {day.Year * 12 + day.Month})");
            await db.Set<SchedulePeriod>().FromSqlInterpolated($"SELECT * FROM \"SchedulePeriod\" WHERE \"Year\" = {day.Year} AND \"Month\" = {day.Month} FOR UPDATE").LoadAsync();
        }
        await accounts.Lock();
        var id = await teams.Actor(actor, admin);
        foreach (var user in await db.Users.OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync()) await availability.LockEmployee(user);
        return id;
    }
    private Task<SchedulePeriod?> Period(int year, int month) => db.Set<SchedulePeriod>().SingleOrDefaultAsync(p => p.Year == year && p.Month == month);
    private async Task<SchedulePeriod> WorkingPeriod(int year, int month)
    {
        var p = await Period(year, month);
        if (p is null) { p = new SchedulePeriod { Year = year, Month = month }; db.Add(p); await db.SaveChangesAsync(); }
        return p;
    }
    private async Task CheckToken(int year, int month, string token)
    {
        var p = await inputs.Read(year, month, review: true);
        if (p.Fingerprint != token) throw new ApiException("STALE_DRAFT", 409);
    }
    private static bool InMonth(Work w, int year, int month) => w.Date.Year == year && w.Date.Month == month;
    private async Task<ScheduleSnapshot> Snapshot(int year, int month, SchedulingProblem p)
    {
        var first = AvailabilityService.MonthStart(year, month);
        var definitions = await db.Set<ShiftTypeDefinition>().AsNoTracking().OrderBy(x => x.DisplayOrder).ToArrayAsync();
        var saved = await db.Set<SchedulingCell>().AsNoTracking().Where(c => c.Date >= first && c.Date < first.AddMonths(1)).ToArrayAsync();
        var names = await db.Users.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.FirstName + " " + x.LastName);
        var cells = definitions.SelectMany(d => Enumerable.Range(0, first.AddMonths(1).DayNumber - first.DayNumber).Select(i => {
            var date = first.AddDays(i); var stored = saved.SingleOrDefault(c => c.Date == date && c.ShiftTypeDefinitionId == d.Id);
            var time = ShiftTimeRanges.Boundaries(date, ShiftTimeRanges.Effective(d));
            return new SnapshotCell(d.Id, date, d.Name, d.DisplayOrder, d.Period.ToString(), ShiftTimeRanges.Effective(d).ToString(),
                stored?.StartsAt ?? time.StartsAt, stored?.EndsAt ?? time.EndsAt, d.RequiredEmployees, d.RequiresShiftLeadQualification,
                p.Work.Where(w => w.DefinitionId == d.Id && w.Date == date).OrderBy(w => w.UserId)
                    .Select(w => new SnapshotEmployee(w.AssignmentId, w.UserId, names[w.UserId], w.Lead)).ToArray());
        })).ToArray();
        var legacy = p.Work.Where(w => w.DefinitionId is null && InMonth(w, year, month)).OrderBy(w => w.AssignmentId)
            .Select(w => new LegacyWork(w.AssignmentId, w.UserId, names[w.UserId], w.Date, w.AnchorType.ToString(), w.Start, w.End, w.Lead)).ToArray();
        return new(year, month, cells, legacy);
    }
    public async Task<RosterProblem[]> Problems(SchedulingProblem p, ScheduleSnapshot snapshot)
    {
        var result = new List<RosterProblem>();
        var leaderIds = await db.Set<OrganizationalTeam>().Where(t => t.LeaderUserId != null).Select(t => t.LeaderUserId!.Value).ToArrayAsync();
        foreach (var w in p.Work.Where(w => InMonth(w, p.Year, p.Month)))
        {
            void Add(string code) => result.Add(new(w.AssignmentId, w.UserId, w.Date, code));
            var employee = p.Employees.SingleOrDefault(e => e.Id == w.UserId);
            if (leaderIds.Contains(w.UserId)) Add("TEAM_LEADER_ASSIGNED");
            else if (employee is null) Add("EMPLOYEE_INELIGIBLE");
            var cell = snapshot.Cells.SingleOrDefault(c => c.DefinitionId == w.DefinitionId && c.Date == w.Date);
            if ((cell?.RequiresShiftLeadQualification == true || w.Lead) && employee?.IsShiftLeadQualified != true) Add("QUALIFICATION_REQUIRED");
            if (p.Preferences.Any(b => b.UserId == w.UserId && b.State == PreferenceType.BLOCKED &&
                ShiftClock.Boundaries(b.Date, b.Type).StartsAt < w.End && ShiftClock.Boundaries(b.Date, b.Type).EndsAt > w.Start)) Add("HARD_BLOCK_CONFLICT");
            if (p.Work.Any(other => other.AssignmentId != w.AssignmentId && other.UserId == w.UserId &&
                (SchedulingProblem.Conflicts(w.Start, w.End, other.Start, other.End) || (w.Date == other.Date && w.AnchorType == other.AnchorType)))) Add("WORK_OVERLAP_OR_CONSECUTIVE");
            if (cell is not null)
            {
                var expected = ShiftTimeRanges.Boundaries(cell.Date, Enum.Parse<ShiftTimeRange>(cell.TimeRange));
                if (w.Start != expected.StartsAt || w.End != expected.EndsAt || w.AnchorType.ToString() != cell.Period) Add("CELL_CONFIGURATION_CHANGED");
            }
        }
        foreach (var c in snapshot.Cells.Where(c => c.Employees.Length > c.RequiredEmployees)) result.Add(new(null, null, c.Date, "STAFFING_EXCEEDED"));
        var inconsistent = await db.Set<ShiftAssignment>().AsNoTracking().Where(a => a.SchedulingCellId != null &&
            (a.Shift.SchedulePeriod.Year == p.Year && a.Shift.SchedulePeriod.Month == p.Month || a.Cell!.SchedulePeriod.Year == p.Year && a.Cell.SchedulePeriod.Month == p.Month) &&
            (a.Cell!.SchedulePeriodId != a.Shift.SchedulePeriodId || a.Cell.Date != a.Shift.ShiftDate))
            .Select(a => new RosterProblem(a.Id, a.UserId, a.Shift.ShiftDate, "SCHEDULE_DATA_INCONSISTENT")).ToArrayAsync();
        result.AddRange(inconsistent);
        return result.Distinct().ToArray();
    }
    private async Task<ReviewResponse> Read(int year, int month)
    {
        var p = await inputs.Read(year, month, review: true);
        var period = await Period(year, month);
        var snapshot = await Snapshot(year, month, p);
        var conflicts = await Problems(p, snapshot);
        var stale = period?.ReviewedInputs != await InputRevision(db);
        var issues = period is null ? 0 : await db.Set<ScheduleIssue>().CountAsync(i => i.SchedulePeriodId == period.Id && (i.Status == "OPEN" || i.Status == "IN_REVIEW"));
        return new(period?.Status.ToString() ?? "DRAFT", period?.Revision ?? 0, p.Fingerprint, stale, !stale && conflicts.Length == 0,
            snapshot.Cells.Sum(c => Math.Max(0, c.RequiredEmployees - c.Employees.Length)), issues, conflicts, snapshot);
    }
    public async Task<ReviewResponse> Review(int year, int month, ClaimsPrincipal actor)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        await teams.Actor(actor, true); var response = await Read(year, month); await tx.CommitAsync(); return response;
    }
    private static void EnsureEditable(SchedulePeriod p) { if (!Editable(p.Status)) throw new ApiException("INVALID_SCHEDULE_TRANSITION", 409); }
    private static CandidateCell Candidate(ShiftTypeDefinition d, DateOnly date)
    {
        var t = ShiftTimeRanges.Boundaries(date, ShiftTimeRanges.Effective(d));
        return new(d.Id, date, t.StartsAt, t.EndsAt, d.RequiredEmployees, d.Version, d.Period, d.RequiresShiftLeadQualification);
    }
    private static string[] Reasons(SchedulingProblem p, CandidateCell cell, Guid user, bool leader)
    {
        var reasons = new List<string>(); var e = p.Employees.SingleOrDefault(e => e.Id == user);
        if (leader) reasons.Add("TEAM_LEADER_ASSIGNED"); else if (e is null) reasons.Add("EMPLOYEE_INELIGIBLE");
        if (cell.Required == 0 || p.Work.Count(w => w.DefinitionId == cell.DefinitionId && w.Date == cell.Date) >= cell.Required) reasons.Add("STAFFING_EXCEEDED");
        if (cell.RequiresShiftLeadQualification && e?.IsShiftLeadQualified != true) reasons.Add("QUALIFICATION_REQUIRED");
        if (p.Preferences.Any(b => b.UserId == user && b.State == PreferenceType.BLOCKED && SchedulingProblem.OverlapsAnchor(cell, b))) reasons.Add("HARD_BLOCK_CONFLICT");
        if (p.Work.Any(w => w.UserId == user && (SchedulingProblem.Conflicts(cell.Start, cell.End, w.Start, w.End) || (w.Date == cell.Date && w.AnchorType == cell.AnchorType)))) reasons.Add("WORK_OVERLAP_OR_CONSECUTIVE");
        return reasons.ToArray();
    }
    public async Task<CandidateResponse[]> Candidates(int year, int month, Guid definitionId, DateOnly date, Guid? replacing, ClaimsPrincipal actor)
    {
        AvailabilityService.MonthStart(year, month); if (date.Year != year || date.Month != month) throw new ApiException("VALIDATION_ERROR");
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        await teams.Actor(actor, true); var p = await inputs.Read(year, month, review: true);
        if (replacing.HasValue && !p.Work.Any(w => w.AssignmentId == replacing && InMonth(w, year, month))) throw new ApiException("NOT_FOUND", 404);
        var sourceRequiresLead = p.Work.Any(w => w.AssignmentId == replacing && w.Lead);
        p = p with { Work = p.Work.Where(w => w.AssignmentId != replacing).ToArray() };
        var d = await db.Set<ShiftTypeDefinition>().AsNoTracking().SingleOrDefaultAsync(d => d.Id == definitionId) ?? throw new ApiException("NOT_FOUND", 404);
        var leaderIds = await db.Set<OrganizationalTeam>().Where(t => t.LeaderUserId != null).Select(t => t.LeaderUserId!.Value).ToArrayAsync();
        var users = await db.Users.AsNoTracking().OrderBy(u => u.LastName).ThenBy(u => u.FirstName).Select(u => new { u.Id, Name = u.FirstName + " " + u.LastName }).ToArrayAsync();
        var result = users.Select(u => { var reasons = Reasons(p, Candidate(d, date) with { RequiresShiftLeadQualification = d.RequiresShiftLeadQualification || sourceRequiresLead }, u.Id, leaderIds.Contains(u.Id)); return new CandidateResponse(u.Id, u.Name, reasons.Length == 0, reasons); }).ToArray();
        await tx.CommitAsync(); return result;
    }
    public async Task<ReviewResponse> Manual(int year, int month, ManualAssignmentRequest input, ClaimsPrincipal actor)
    {
        if (input.Operation is not ("ASSIGN" or "REMOVE" or "REPLACE" or "MOVE")) throw new ApiException("VALIDATION_ERROR");
        await using var tx = await db.Database.BeginTransactionAsync(); var actorId = await Lock(year, month, actor);
        await CheckToken(year, month, input.ExpectedToken); var period = await WorkingPeriod(year, month); EnsureEditable(period);
        ShiftAssignment? old = null; object? before = null;
        if (input.Operation != "ASSIGN")
        {
            old = await db.Set<ShiftAssignment>().Include(a => a.Shift).Include(a => a.Cell).Include(a => a.Tasks)
                .SingleOrDefaultAsync(a => a.Id == input.AssignmentId && a.Shift.SchedulePeriodId == period.Id) ?? throw new ApiException("NOT_FOUND", 404);
            if (old.Tasks.Count > 0) throw new ApiException("ASSIGNMENT_HAS_TASKS", 409);
            before = new { old.Id, old.UserId, old.ShiftId, old.SchedulingCellId, old.IsShiftLead, old.IsLocked, Date = old.Cell?.Date ?? old.Shift.ShiftDate,
                StartsAt = old.Cell?.StartsAt ?? old.Shift.StartsAt, EndsAt = old.Cell?.EndsAt ?? old.Shift.EndsAt, DefinitionId = old.Cell?.ShiftTypeDefinitionId };
            db.Remove(old); await db.SaveChangesAsync();
        }
        ShiftAssignment? added = null; object? after = null;
        if (input.Operation != "REMOVE")
        {
            var userId = input.Operation == "MOVE" ? old!.UserId : input.UserId ?? throw new ApiException("VALIDATION_ERROR");
            var date = input.Operation == "REPLACE" ? old!.Cell?.Date ?? old.Shift.ShiftDate : input.Date ?? throw new ApiException("VALIDATION_ERROR");
            var defId = input.Operation == "REPLACE" ? old!.Cell?.ShiftTypeDefinitionId ?? input.DefinitionId : input.DefinitionId;
            if (date.Year != year || date.Month != month) throw new ApiException("VALIDATION_ERROR");
            var d = await db.Set<ShiftTypeDefinition>().SingleOrDefaultAsync(d => d.Id == defId) ?? throw new ApiException("NOT_FOUND", 404);
            var p = await inputs.Read(year, month, review: true); var candidate = Candidate(d, date) with { RequiresShiftLeadQualification = d.RequiresShiftLeadQualification || old?.IsShiftLead == true };
            var leader = await db.Set<OrganizationalTeam>().AnyAsync(t => t.LeaderUserId == userId);
            var reasons = Reasons(p, candidate, userId, leader); if (reasons.Length > 0) throw new ApiException(reasons[0], 409);
            var cell = await db.Set<SchedulingCell>().SingleOrDefaultAsync(c => c.Date == date && c.ShiftTypeDefinitionId == d.Id);
            if (cell is not null && (cell.StartsAt != candidate.Start || cell.EndsAt != candidate.End))
            {
                if (await db.Set<ShiftAssignment>().AnyAsync(a => a.SchedulingCellId == cell.Id)) throw new ApiException("CELL_CONFIGURATION_CHANGED", 409);
                // An empty cell can adopt current configuration without retiming anyone's work.
                cell.StartsAt = candidate.Start; cell.EndsAt = candidate.End;
                cell.RequiredEmployees = d.RequiredEmployees; cell.ConfigurationVersion = d.Version;
            }
            if (cell is null)
            {
                var run = new SchedulingRun { Id = Guid.NewGuid(), Year = year, Month = month, SavedByUserId = actorId, SavedAt = Now, SolverStatus = "MANUAL" };
                db.Add(run); cell = new SchedulingCell { Date = date, ShiftTypeDefinitionId = d.Id, SchedulePeriodId = period.Id,
                    StartsAt = candidate.Start, EndsAt = candidate.End, RequiredEmployees = d.RequiredEmployees, ConfigurationVersion = d.Version, SchedulingRunId = run.Id }; db.Add(cell);
            }
            var shift = await db.Set<Shift>().SingleOrDefaultAsync(s => s.ShiftDate == date && s.ShiftType == d.Period);
            if (shift is null) { var t = ShiftClock.Boundaries(date, d.Period); shift = new Shift { ShiftDate = date, ShiftType = d.Period, SchedulePeriodId = period.Id, StartsAt = t.StartsAt, EndsAt = t.EndsAt }; db.Add(shift); }
            added = new ShiftAssignment { UserId = userId, ShiftId = shift.Id, SchedulingCellId = cell.Id, IsLocked = true, IsShiftLead = old?.IsShiftLead ?? false, CreatedAt = Now };
            db.Add(added);
            after = new { added.Id, added.UserId, added.ShiftId, added.SchedulingCellId, added.IsLocked, Date = date, DefinitionId = d.Id, StartsAt = candidate.Start, EndsAt = candidate.End };
        }
        period.Revision++; period.UpdatedAt = Now;
        db.Add(new ScheduleEditAudit { SchedulePeriodId = period.Id, ActorUserId = actorId, OccurredAt = Now, Operation = input.Operation,
            Before = JsonSerializer.Serialize(before), After = JsonSerializer.Serialize(after) });
        await db.SaveChangesAsync(); var response = await Read(year, month); await tx.CommitAsync(); return response;
    }
    public async Task<ReviewResponse> Acknowledge(int year, int month, ReviewTokenRequest input, ClaimsPrincipal actor)
    {
        await using var tx = await db.Database.BeginTransactionAsync(); await Lock(year, month, actor); await CheckToken(year, month, input.ExpectedToken);
        var period = await WorkingPeriod(year, month); EnsureEditable(period); var review = await Read(year, month);
        if (review.Conflicts.Length > 0) throw new ApiException("ROSTER_CONFLICT", 409);
        period.ReviewedInputs = await InputRevision(db); period.Revision++; period.UpdatedAt = Now; await db.SaveChangesAsync();
        var result = await Read(year, month); await tx.CommitAsync(); return result;
    }
    public async Task<ReviewResponse> Correction(int year, int month, ReviewTokenRequest input, ClaimsPrincipal actor)
    {
        await using var tx = await db.Database.BeginTransactionAsync(); var actorId = await Lock(year, month, actor); await CheckToken(year, month, input.ExpectedToken);
        var period = await Period(year, month) ?? throw new ApiException("PERIOD_NOT_FOUND", 404);
        if (period.Status != ScheduleStatus.PRELIMINARY_PUBLISHED) throw new ApiException("INVALID_SCHEDULE_TRANSITION", 409);
        period.Status = ScheduleStatus.UNDER_CORRECTION; period.Revision++; period.UpdatedAt = Now;
        db.Add(new ScheduleEditAudit { SchedulePeriodId = period.Id, ActorUserId = actorId, OccurredAt = Now, Operation = "UNDER_CORRECTION" });
        await db.SaveChangesAsync(); var result = await Read(year, month); await tx.CommitAsync(); return result;
    }
    private static PublicationResponse Publication(SchedulePublication p) => new(p.Id, p.VersionNumber, p.PublicationType, p.PublishedByUserId, p.PublishedAt, p.Notes,
        JsonSerializer.Deserialize<ScheduleSnapshot>(p.Snapshot), p.AcknowledgedShortages, p.AcknowledgedOpenIssues, p.OpenIssueCount);
    public async Task<PublicationResponse> Publish(int year, int month, string type, PublishScheduleRequest input, ClaimsPrincipal actor)
    {
        if (type is not ("PRELIMINARY" or "FINAL")) throw new ApiException("VALIDATION_ERROR");
        await using var tx = await db.Database.BeginTransactionAsync(); var actorId = await Lock(year, month, actor); await CheckToken(year, month, input.ExpectedToken);
        var period = await Period(year, month) ?? throw new ApiException("PERIOD_NOT_FOUND", 404);
        if (type == "PRELIMINARY" ? !Editable(period.Status) : period.Status is not (ScheduleStatus.PRELIMINARY_PUBLISHED or ScheduleStatus.UNDER_CORRECTION)) throw new ApiException("INVALID_SCHEDULE_TRANSITION", 409);
        if (type == "FINAL" && !await db.Set<SchedulePublication>().AnyAsync(p => p.SchedulePeriodId == period.Id && p.PublicationType == "PRELIMINARY")) throw new ApiException("INVALID_SCHEDULE_TRANSITION", 409);
        var review = await Read(year, month);
        if (review.Conflicts.Length > 0) throw new ApiException("ROSTER_CONFLICT", 409);
        if (review.NeedsReview) throw new ApiException("STALE_DRAFT", 409);
        if (review.Shortage > 0 && !input.AcknowledgeShortages) throw new ApiException("SHORTAGES_REQUIRE_ACKNOWLEDGMENT", 409);
        if (type == "FINAL" && review.OpenIssues > 0 && !input.AcknowledgeOpenIssues) throw new ApiException("ISSUES_REQUIRE_ACKNOWLEDGMENT", 409);
        var version = (await db.Set<SchedulePublication>().Where(p => p.SchedulePeriodId == period.Id).MaxAsync(p => (int?)p.VersionNumber) ?? 0) + 1;
        var publication = new SchedulePublication { SchedulePeriodId = period.Id, PublishedByUserId = actorId, PublishedAt = Now,
            AcknowledgedShortages = input.AcknowledgeShortages, AcknowledgedOpenIssues = input.AcknowledgeOpenIssues, OpenIssueCount = review.OpenIssues,
            VersionNumber = version, PublicationType = type, Snapshot = JsonSerializer.Serialize(review.Draft), Notes = input.Notes?.Trim(), InputFingerprint = review.Token };
        db.Add(publication); period.Status = type == "FINAL" ? ScheduleStatus.FINAL_PUBLISHED : ScheduleStatus.PRELIMINARY_PUBLISHED;
        period.Revision++; period.UpdatedAt = Now; await db.SaveChangesAsync(); await tx.CommitAsync(); return Publication(publication);
    }
    public async Task<PublicationResponse[]> History(int year, int month, ClaimsPrincipal actor)
    {
        AvailabilityService.MonthStart(year, month); await teams.Actor(actor, true);
        var period = await Period(year, month); if (period is null) return [];
        return (await db.Set<SchedulePublication>().AsNoTracking().Where(p => p.SchedulePeriodId == period.Id).OrderByDescending(p => p.VersionNumber).ToArrayAsync()).Select(Publication).ToArray();
    }
    private async Task<Guid> Employee(ClaimsPrincipal actor)
    {
        var id = await teams.Actor(actor);
        if (!await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id where ur.UserId == id && r.NormalizedName == "EMPLOYEE" select ur).AnyAsync()) throw new ApiException("FORBIDDEN", 403);
        return id;
    }
    public async Task<object[]> PublishedPeriods(ClaimsPrincipal actor)
    {
        await Employee(actor);
        return await db.Set<SchedulePublication>().AsNoTracking().Where(p => p.PublicationType == "PRELIMINARY" || p.PublicationType == "FINAL")
            .Join(db.Set<SchedulePeriod>(), p => p.SchedulePeriodId, p => p.Id, (pub, p) => new { p.Year, p.Month }).Distinct().OrderByDescending(p => p.Year).ThenByDescending(p => p.Month).Cast<object>().ToArrayAsync();
    }
    public async Task<PublicationResponse> Mine(int year, int month, ClaimsPrincipal actor)
    {
        AvailabilityService.MonthStart(year, month); await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead); var id = await Employee(actor);
        var period = await Period(year, month) ?? throw new ApiException("NOT_FOUND", 404);
        var pub = await db.Set<SchedulePublication>().AsNoTracking().Where(p => p.SchedulePeriodId == period.Id && (p.PublicationType == "PRELIMINARY" || p.PublicationType == "FINAL"))
            .OrderByDescending(p => p.VersionNumber).FirstOrDefaultAsync() ?? throw new ApiException("NOT_FOUND", 404);
        var response = Publication(pub); var s = response.Snapshot!;
        // Personal schedule privacy: never return another employee's identity or assignments.
        response = response with { Snapshot = s with { Cells = s.Cells.Where(c => c.Employees.Any(e => e.UserId == id)).Select(c => c with { Employees = c.Employees.Where(e => e.UserId == id).ToArray() }).ToArray(), LegacyAssignments = s.LegacyAssignments.Where(a => a.UserId == id).ToArray() } };
        await tx.CommitAsync(); return response;
    }
    public async Task<ScheduleIssue> Report(int year, int month, CreateIssueRequest input, ClaimsPrincipal actor)
    {
        await using var tx = await db.Database.BeginTransactionAsync(); var id = await Lock(year, month, actor, false); await Employee(actor);
        var period = await Period(year, month) ?? throw new ApiException("NOT_FOUND", 404);
        if (period.Status is not (ScheduleStatus.PRELIMINARY_PUBLISHED or ScheduleStatus.UNDER_CORRECTION)) throw new ApiException("ISSUE_REPORTING_CLOSED", 409);
        var latest = await db.Set<SchedulePublication>().Where(p => p.SchedulePeriodId == period.Id).OrderByDescending(p => p.VersionNumber).FirstOrDefaultAsync();
        if (latest is null || latest.Id != input.PublicationId || latest.PublicationType != "PRELIMINARY") throw new ApiException("STALE_PUBLICATION", 409);
        var snapshot = JsonSerializer.Deserialize<ScheduleSnapshot>(latest.Snapshot)!;
        if (!snapshot.Cells.SelectMany(c => c.Employees).Any(e => e.AssignmentId == input.AssignmentId && e.UserId == id)
            && !snapshot.LegacyAssignments.Any(e => e.AssignmentId == input.AssignmentId && e.UserId == id)) throw new ApiException("FORBIDDEN", 403);
        var issue = new ScheduleIssue { SchedulePeriodId = period.Id, PublicationId = latest.Id, AssignmentId = input.AssignmentId,
            ReporterUserId = id, Explanation = input.Explanation.Trim(), CreatedAt = Now, UpdatedAt = Now };
        db.Add(issue); period.Revision++; await db.SaveChangesAsync(); await tx.CommitAsync(); return issue;
    }
    public async Task<ScheduleIssue[]> Issues(int year, int month, ClaimsPrincipal actor, bool admin)
    {
        AvailabilityService.MonthStart(year, month); var id = admin ? await teams.Actor(actor, true) : await Employee(actor);
        var period = await Period(year, month); if (period is null) return [];
        return await db.Set<ScheduleIssue>().AsNoTracking().Where(i => i.SchedulePeriodId == period.Id && (admin || i.ReporterUserId == id)).OrderByDescending(i => i.CreatedAt).ToArrayAsync();
    }
    public async Task<ScheduleIssue> UpdateIssue(int year, int month, Guid issueId, UpdateIssueRequest input, ClaimsPrincipal actor)
    {
        if (input.Status is not ("OPEN" or "IN_REVIEW" or "RESOLVED" or "REJECTED")) throw new ApiException("VALIDATION_ERROR");
        await using var tx = await db.Database.BeginTransactionAsync(); var actorId = await Lock(year, month, actor);
        var period = await Period(year, month) ?? throw new ApiException("PERIOD_NOT_FOUND", 404);
        var issue = await db.Set<ScheduleIssue>().SingleOrDefaultAsync(i => i.Id == issueId && i.SchedulePeriodId == period.Id) ?? throw new ApiException("NOT_FOUND", 404);
        if (issue.Version != input.ExpectedVersion) throw new ApiException("CONFLICT", 409);
        db.Add(new ScheduleEditAudit { SchedulePeriodId = period.Id, ActorUserId = actorId, OccurredAt = Now, Operation = "ISSUE_STATUS", Before = JsonSerializer.Serialize(new { issue.Id, issue.Status, issue.Response }), After = JsonSerializer.Serialize(new { issue.Id, input.Status, input.Response }) });
        issue.Status = input.Status; issue.Response = input.Response?.Trim(); issue.ReviewedByUserId = actorId; issue.UpdatedAt = Now; issue.Version++; period.Revision++;
        await db.SaveChangesAsync(); await tx.CommitAsync(); return issue;
    }
    public async Task<ScheduleEditAudit[]> Audit(int year, int month, ClaimsPrincipal actor)
    {
        AvailabilityService.MonthStart(year, month); await teams.Actor(actor, true); var p = await Period(year, month);
        if (p is null) return []; return await db.Set<ScheduleEditAudit>().AsNoTracking().Where(a => a.SchedulePeriodId == p.Id).OrderByDescending(a => a.OccurredAt).ToArrayAsync();
    }
}
