using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Api;
using ShiftManager.Domain;
using ShiftManager.Services.AutomaticScheduling;
namespace ShiftManager.Services;

public sealed partial class ScheduleReviewService
{
    private async Task<ShiftExchangeAssignment[]> ExchangeSource(Guid publicationId, Guid assignmentId, Guid requester)
    {
        var publication = await db.Set<SchedulePublication>().AsNoTracking().SingleOrDefaultAsync(p => p.Id == publicationId) ?? throw new ApiException("NOT_FOUND", 404);
        if ((await VisibleLatest(publication.SchedulePeriodId)).Id != publicationId) throw new ApiException("STALE_PUBLICATION", 409);
        var snapshot = JsonSerializer.Deserialize<ScheduleSnapshot>(publication.Snapshot)!;
        var cell = snapshot.Cells.SingleOrDefault(c => c.Employees.Any(e => e.AssignmentId == assignmentId && e.UserId == requester)) ?? throw new ApiException("FORBIDDEN", 403);
        var dates = cell.Semantic == SchedulingRules.OnCall ? new[] { SchedulingRules.Friday(cell.Date), SchedulingRules.Friday(cell.Date).AddDays(1) } : new[] { cell.Date };
        var result = new List<ShiftExchangeAssignment>();
        foreach (var date in dates)
        {
            var period = await Period(date.Year, date.Month) ?? throw new ApiException("EXCHANGE_UNAVAILABLE", 409);
            var active = await VisibleLatest(period.Id);
            var published = JsonSerializer.Deserialize<ScheduleSnapshot>(active.Snapshot)!;
            var target = published.Cells.SingleOrDefault(c => c.Date == date && c.DefinitionId == cell.DefinitionId && c.Employees.Any(e => e.UserId == requester))
                ?? throw new ApiException("EXCHANGE_UNAVAILABLE", 409);
            var source = target.Employees.Single(e => e.UserId == requester);
            if (target.StartsAt <= Now || !target.Applicable) throw new ApiException("EXCHANGE_UNAVAILABLE", 409);
            result.Add(new() { PublicationId = active.Id, SourceAssignmentId = source.AssignmentId, DefinitionId = target.DefinitionId,
                Date = date, StartsAt = target.StartsAt, EndsAt = target.EndsAt, IsShiftLead = source.IsShiftLead, Note = source.Note });
        }
        // Prevent a request by a formerly eligible account, even if its old snapshot still exists.
        var problem = await inputs.Read(cell.Date.Year, cell.Date.Month, review: true);
        if (!problem.Employees.Any(e => e.Id == requester)) throw new ApiException("EMPLOYEE_INELIGIBLE", 409);
        return result.ToArray();
    }
    private async Task ValidateExchangeSources(IEnumerable<ShiftExchangeAssignment> sources, Guid requester)
    {
        foreach (var source in sources)
        {
            var period = await Period(source.Date.Year, source.Date.Month) ?? throw new ApiException("EXCHANGE_UNAVAILABLE", 409);
            if ((await VisibleLatest(period.Id)).Id != source.PublicationId || source.StartsAt <= Now) throw new ApiException("EXCHANGE_UNAVAILABLE", 409);
            var assignment = await db.Set<ShiftAssignment>().AsNoTracking().Include(a => a.Cell).Include(a => a.Tasks)
                .SingleOrDefaultAsync(a => a.Id == source.SourceAssignmentId && a.UserId == requester);
            if (assignment?.Cell is null || assignment.Cell.Date != source.Date || assignment.Cell.ShiftTypeDefinitionId != source.DefinitionId
                || assignment.Cell.StartsAt != source.StartsAt || assignment.Cell.EndsAt != source.EndsAt || assignment.IsShiftLead != source.IsShiftLead || assignment.Note != source.Note)
                throw new ApiException("EXCHANGE_UNAVAILABLE", 409);
            if (assignment.Tasks.Count > 0) throw new ApiException("ASSIGNMENT_HAS_TASKS", 409);
        }
    }
    private async Task<string[]> ReplacementReasons(ShiftExchangeAssignment[] sources, Guid requester, Guid replacement)
    {
        if (requester == replacement) return ["EMPLOYEE_INELIGIBLE"];
        await ValidateExchangeSources(sources, requester);
        var reasons = new List<string>();
        foreach (var group in sources.GroupBy(s => (s.Date.Year, s.Date.Month)))
        {
            var problem = await inputs.Read(group.Key.Year, group.Key.Month, review: true);
            if (!problem.Employees.Any(e => e.Id == requester)) reasons.Add("EMPLOYEE_INELIGIBLE");
            problem = problem with { Work = problem.Work.Where(w => !sources.Any(s => s.SourceAssignmentId == w.AssignmentId)).ToArray() };
            foreach (var source in sources.OrderBy(s => s.Date))
            {
                var definition = await db.Set<ShiftTypeDefinition>().AsNoTracking().SingleAsync(d => d.Id == source.DefinitionId);
                var candidate = Candidate(definition, source.Date) with { RequiresShiftLeadQualification = definition.RequiresShiftLeadQualification || source.IsShiftLead };
                if (candidate.Start != source.StartsAt || candidate.End != source.EndsAt) reasons.Add("CELL_CONFIGURATION_CHANGED");
                reasons.AddRange(Reasons(problem, candidate, replacement, false, pairOperation: sources.Length == 2));
                problem = problem with { Work = problem.Work.Append(SchedulingRules.Proposed(replacement, candidate)).ToArray() };
            }
            reasons.AddRange(SchedulingRules.Violations(problem.Work, group.Key.Year, group.Key.Month).Where(v => v.UserId == replacement).Select(v => v.Code));
        }
        return reasons.Distinct().ToArray();
    }
    public async Task<ExchangeAssignmentResponse[]> ExchangeAssignments(int year, int month, ClaimsPrincipal actor)
    {
        AvailabilityService.MonthStart(year, month);
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead); var id = await Employee(actor);
        var period = await Period(year, month) ?? throw new ApiException("NOT_FOUND", 404); var publication = await VisibleLatest(period.Id);
        var snapshot = JsonSerializer.Deserialize<ScheduleSnapshot>(publication.Snapshot)!;
        var reserved = await db.Set<ShiftExchangeReservation>().Select(r => r.AssignmentId).ToArrayAsync();
        var result = new List<ExchangeAssignmentResponse>();
        foreach (var cell in snapshot.Cells.Where(c => c.StartsAt > Now && c.Employees.Any(e => e.UserId == id)))
        {
            var assignment = cell.Employees.Single(e => e.UserId == id);
            if (reserved.Contains(assignment.AssignmentId)) continue;
            try
            {
                var sources = await ExchangeSource(publication.Id, assignment.AssignmentId, id);
                await ValidateExchangeSources(sources, id);
                result.Add(new(assignment.AssignmentId, publication.Id, cell.DefinitionId, cell.Date, cell.StartsAt, cell.EndsAt, assignment.IsShiftLead, cell.Name));
            }
            catch (ApiException e) when (e.Status is 404 or 409) { /* Changed, past, or paired unpublished assignments are ineligible. */ }
        }
        await tx.CommitAsync(); return result.ToArray();
    }
    public async Task<CandidateResponse[]> ExchangeCandidates(int year, int month, Guid publicationId, Guid assignmentId, ClaimsPrincipal actor)
    {
        AvailabilityService.MonthStart(year, month);
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead); var id = await Employee(actor);
        var sources = await ExchangeSource(publicationId, assignmentId, id);
        if (!sources.Any(s => s.Date.Year == year && s.Date.Month == month)) throw new ApiException("VALIDATION_ERROR");
        var problem = await inputs.Read(year, month, review: true); var result = new List<CandidateResponse>();
        foreach (var employee in problem.Employees.Where(e => e.Id != id))
            if ((await ReplacementReasons(sources, id, employee.Id)).Length == 0) result.Add(new(employee.Id, employee.Name, true, [], employee.IsShiftLeadQualified));
        await tx.CommitAsync(); return result.OrderBy(c => c.Name).ToArray();
    }
    public async Task<ExchangeResponse> CreateExchange(int year, int month, ExchangeCreateRequest input, ClaimsPrincipal actor)
    {
        await using var tx = await db.Database.BeginTransactionAsync(); var id = await Lock(year, month, actor, false); await Employee(actor);
        var sources = await ExchangeSource(input.PublicationId, input.AssignmentId, id);
        if (!sources.Any(s => s.Date.Year == year && s.Date.Month == month)) throw new ApiException("VALIDATION_ERROR");
        var reasons = await ReplacementReasons(sources, id, input.ReplacementUserId); if (reasons.Length > 0) throw new ApiException(reasons[0], 409);
        if (await db.Set<ShiftExchangeReservation>().AnyAsync(r => sources.Select(s => s.SourceAssignmentId).Contains(r.AssignmentId))) throw new ApiException("EXCHANGE_PENDING", 409);
        var period = await Period(year, month) ?? throw new ApiException("NOT_FOUND", 404);
        var request = new ShiftExchange { SchedulePeriodId = period.Id, RequesterUserId = id, ReplacementUserId = input.ReplacementUserId, Assignments = sources, CreatedAt = Now, UpdatedAt = Now };
        db.Add(request);
        foreach (var source in sources) db.Add(new ShiftExchangeReservation { AssignmentId = source.SourceAssignmentId, ShiftExchangeId = request.Id });
        ExchangeAudit(request, id, null); await db.SaveChangesAsync(); var result = await ExchangeResponseFor(request, true); await tx.CommitAsync(); return result;
    }
    private void ExchangeAudit(ShiftExchange r, Guid actorId, string? previous) => db.Add(new ScheduleEditAudit {
        SchedulePeriodId = r.SchedulePeriodId, ActorUserId = actorId, OccurredAt = Now, Operation = "EXCHANGE_STATUS",
        Before = JsonSerializer.Serialize(new { RequestId = r.Id, Status = previous }),
        After = JsonSerializer.Serialize(new { RequestId = r.Id, r.Status, r.RequesterUserId, r.ReplacementUserId, r.Reason }) });
    private async Task TransitionExchange(ShiftExchange r, string status, Guid actorId, string? reason = null)
    {
        var previous = r.Status; r.Status = status; r.Version++; r.UpdatedAt = Now; r.Reason = reason;
        ExchangeAudit(r, actorId, previous);
        if (status is "REJECTED" or "CANCELLED" or "SUPERSEDED" or "APPLIED")
            db.RemoveRange(await db.Set<ShiftExchangeReservation>().Where(x => x.ShiftExchangeId == r.Id).ToArrayAsync());
    }
    private async Task<ExchangeResponse> ExchangeResponseFor(ShiftExchange r, bool admin)
    {
        var names = await db.Users.Where(u => u.Id == r.RequesterUserId || u.Id == r.ReplacementUserId).ToDictionaryAsync(u => u.Id, u => u.FirstName + " " + u.LastName);
        var visible = admin;
        if (!admin)
        {
            visible = true;
            foreach (var a in r.Assignments)
            {
                var period = await Period(a.Date.Year, a.Date.Month);
                try { if (period is null) { visible = false; break; } await VisibleLatest(period.Id); }
                catch (ApiException e) when (e.Status == 404) { visible = false; break; }
                if (!await EmployeePublications().AnyAsync(p => p.Id == a.PublicationId)) { visible = false; break; }
            }
        }
        var details = new List<ExchangeAssignmentResponse>();
        if (visible)
            foreach (var a in r.Assignments)
            {
                var publication = await db.Set<SchedulePublication>().AsNoTracking().SingleAsync(p => p.Id == a.PublicationId);
                var cell = JsonSerializer.Deserialize<ScheduleSnapshot>(publication.Snapshot)!.Cells.Single(c => c.Date == a.Date && c.DefinitionId == a.DefinitionId);
                details.Add(new(a.SourceAssignmentId, a.PublicationId, a.DefinitionId, a.Date, a.StartsAt, a.EndsAt, a.IsShiftLead, cell.Name));
            }
        return new(r.Id, r.RequesterUserId, names[r.RequesterUserId], r.ReplacementUserId, names[r.ReplacementUserId], r.Status, r.Version, r.Reason, r.CreatedAt, details.ToArray());
    }
    public async Task<ExchangeResponse[]> Exchanges(ClaimsPrincipal actor, bool admin = false, int? year = null, int? month = null)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        var id = admin ? await teams.Actor(actor, true) : await Employee(actor);
        var query = db.Set<ShiftExchange>().AsNoTracking().Include(r => r.Assignments).Where(r => admin || r.RequesterUserId == id || r.ReplacementUserId == id);
        if (year.HasValue && month.HasValue)
        {
            AvailabilityService.MonthStart(year.Value, month.Value);
            query = query.Where(r => r.Assignments.Any(a => a.Date.Year == year && a.Date.Month == month));
        }
        var result = new List<ExchangeResponse>();
        foreach (var request in await query.OrderByDescending(r => r.CreatedAt).ToArrayAsync()) result.Add(await ExchangeResponseFor(request, admin));
        await tx.CommitAsync(); return result.ToArray();
    }
    public async Task<ExchangeResponse> DecideExchange(Guid requestId, ExchangeDecisionRequest input, ClaimsPrincipal actor, bool admin = false)
    {
        if (input.Decision is not ("ACCEPT" or "REJECT" or "CANCEL" or "APPROVE") || input.Reason?.Length > 2000) throw new ApiException("VALIDATION_ERROR");
        var route = await (from r in db.Set<ShiftExchange>() join p in db.Set<SchedulePeriod>() on r.SchedulePeriodId equals p.Id where r.Id == requestId select new { p.Year, p.Month }).SingleOrDefaultAsync() ?? throw new ApiException("NOT_FOUND", 404);
        await using var tx = await db.Database.BeginTransactionAsync(); var id = await Lock(route.Year, route.Month, actor, admin);
        if (!admin) await Employee(actor);
        var request = await db.Set<ShiftExchange>().Include(r => r.Assignments).SingleAsync(r => r.Id == requestId);
        if (!admin && id != request.RequesterUserId && id != request.ReplacementUserId) throw new ApiException("FORBIDDEN", 403);
        if (request.Version != input.ExpectedVersion) throw new ApiException("CONFLICT", 409);
        var waiting = request.Status is "AWAITING_REPLACEMENT" or "AWAITING_ADMIN";
        if (!waiting) throw new ApiException("EXCHANGE_UNAVAILABLE", 409);
        if (input.Decision == "CANCEL")
        {
            if (admin || id != request.RequesterUserId) throw new ApiException("FORBIDDEN", 403);
            await TransitionExchange(request, "CANCELLED", id, input.Reason);
        }
        else if (input.Decision == "REJECT")
        {
            if (!admin && (id != request.ReplacementUserId || request.Status != "AWAITING_REPLACEMENT")) throw new ApiException("FORBIDDEN", 403);
            await TransitionExchange(request, "REJECTED", id, input.Reason);
        }
        else
        {
            if (input.Decision == "ACCEPT" ? admin || id != request.ReplacementUserId || request.Status != "AWAITING_REPLACEMENT" : !admin || request.Status != "AWAITING_ADMIN")
                throw new ApiException("FORBIDDEN", 403);
            var sources = request.Assignments.ToArray(); var reasons = await ReplacementReasons(sources, request.RequesterUserId, request.ReplacementUserId);
            if (reasons.Length > 0) throw new ApiException(reasons[0], 409);
            if (input.Decision == "ACCEPT") await TransitionExchange(request, "AWAITING_ADMIN", id);
            else
            {
                // Use the audited manual replacement, joining this transaction. Published JSON stays immutable.
                foreach (var group in sources.GroupBy(s => (s.Date.Year, s.Date.Month)))
                {
                    var period = (await Period(group.Key.Year, group.Key.Month))!;
                    if (period.Status == ScheduleStatus.PRELIMINARY_PUBLISHED)
                    {
                        period.Status = ScheduleStatus.UNDER_CORRECTION; period.Revision++; period.UpdatedAt = Now;
                        db.Add(new ScheduleEditAudit { SchedulePeriodId = period.Id, ActorUserId = id, OccurredAt = Now, Operation = "UNDER_CORRECTION" });
                    }
                }
                await db.SaveChangesAsync();
                var source = sources.OrderBy(a => a.Date).First();
                var p = await inputs.Read(source.Date.Year, source.Date.Month, review: true);
                await Manual(source.Date.Year, source.Date.Month, new(p.Fingerprint, "REPLACE", source.SourceAssignmentId, request.ReplacementUserId, source.DefinitionId, source.Date, source.Note), actor, alreadyLocked: true);
                // Account lock acquisition clears the identity map; reload before recording the result.
                request = await db.Set<ShiftExchange>().Include(r => r.Assignments).SingleAsync(r => r.Id == requestId);
                foreach (var a in request.Assignments)
                    a.ReplacementAssignmentId = await db.Set<ShiftAssignment>().Where(w => w.UserId == request.ReplacementUserId && w.Cell != null && w.Cell.Date == a.Date && w.Cell.ShiftTypeDefinitionId == a.DefinitionId).Select(w => w.Id).SingleAsync();
                await TransitionExchange(request, "APPROVED_PENDING_PUBLICATION", id, input.Reason);
            }
        }
        await db.SaveChangesAsync(); var response = await ExchangeResponseFor(request, admin); await tx.CommitAsync(); return response;
    }
    public async Task<ReviewResponse[]> ExchangePublicationReview(Guid requestId, ClaimsPrincipal actor)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        await teams.Actor(actor, true);
        var r = await db.Set<ShiftExchange>().AsNoTracking().Include(r => r.Assignments).SingleOrDefaultAsync(r => r.Id == requestId) ?? throw new ApiException("NOT_FOUND", 404);
        if (r.Status != "APPROVED_PENDING_PUBLICATION") throw new ApiException("EXCHANGE_UNAVAILABLE", 409);
        var result = new List<ReviewResponse>();
        foreach (var date in r.Assignments.Select(a => new { a.Date.Year, a.Date.Month }).Distinct().OrderBy(a => a.Year).ThenBy(a => a.Month))
            result.Add(await Read(date.Year, date.Month));
        await tx.CommitAsync(); return result.ToArray();
    }
    public async Task<PublicationResponse[]> PublishExchange(Guid requestId, ExchangePublicationRequest input, ClaimsPrincipal actor)
    {
        if (input.Periods is null || input.Periods.Length is < 1 or > 2 || input.Notes?.Length > 2000) throw new ApiException("VALIDATION_ERROR");
        var route = await (from r in db.Set<ShiftExchange>() join p in db.Set<SchedulePeriod>() on r.SchedulePeriodId equals p.Id where r.Id == requestId select new { p.Year, p.Month }).SingleOrDefaultAsync() ?? throw new ApiException("NOT_FOUND", 404);
        await using var tx = await db.Database.BeginTransactionAsync(); await Lock(route.Year, route.Month, actor);
        var request = await db.Set<ShiftExchange>().Include(r => r.Assignments).SingleAsync(r => r.Id == requestId);
        if (request.Status != "APPROVED_PENDING_PUBLICATION") throw new ApiException("EXCHANGE_UNAVAILABLE", 409);
        var dates = request.Assignments.Select(a => (a.Date.Year, a.Date.Month)).Distinct().Order().ToArray();
        if (input.Periods.Length != dates.Length || dates.Any(d => input.Periods.Count(p => p.Year == d.Year && p.Month == d.Month) != 1)) throw new ApiException("VALIDATION_ERROR");
        // Verify every token before the first publication changes a neighboring month's fingerprint.
        foreach (var p in input.Periods) await CheckToken(p.Year, p.Month, p.ExpectedToken);
        var result = new List<PublicationResponse>();
        foreach (var date in dates)
        {
            var review = await Read(date.Year, date.Month);
            var type = review.FinalRevision ? "FINAL" : "PRELIMINARY";
            result.Add(await Publish(date.Year, date.Month, type,
                new(review.Token, input.AcknowledgeShortages, input.AcknowledgeOpenIssues, input.Notes), actor, atomicPair: true));
        }
        await tx.CommitAsync(); return result.ToArray();
    }
    private async Task ReconcileExchanges(SchedulePublication publication, Guid actorId)
    {
        var snapshot = JsonSerializer.Deserialize<ScheduleSnapshot>(publication.Snapshot)!;
        foreach (var request in await db.Set<ShiftExchange>().Include(r => r.Assignments).Where(r =>
            r.Status != "APPLIED" && r.Status != "REJECTED" && r.Status != "CANCELLED" && r.Status != "SUPERSEDED" &&
            r.Assignments.Any(a => a.Date.Year == snapshot.Year && a.Date.Month == snapshot.Month)).ToArrayAsync())
        {
            if (request.Status != "APPROVED_PENDING_PUBLICATION") { await TransitionExchange(request, "SUPERSEDED", actorId, "פורסמה גרסה חדשה"); continue; }
            var targets = request.Assignments.Where(a => a.Date.Year == snapshot.Year && a.Date.Month == snapshot.Month).ToArray();
            if (targets.Any(a => !snapshot.Cells.Any(c => c.Date == a.Date && c.DefinitionId == a.DefinitionId && c.Employees.Any(e => e.AssignmentId == a.ReplacementAssignmentId && e.UserId == request.ReplacementUserId))))
            { await TransitionExchange(request, "SUPERSEDED", actorId, "התיקון המאושר השתנה לפני הפרסום"); continue; }
            foreach (var target in targets) target.AppliedPublicationId = publication.Id;
            if (request.Assignments.All(a => a.AppliedPublicationId.HasValue)) await TransitionExchange(request, "APPLIED", actorId);
        }
    }
}
