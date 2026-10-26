using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Api;
using ShiftManager.Data;
using ShiftManager.Domain;
namespace ShiftManager.Services.AutomaticScheduling;

public sealed class GeneratedDraftService(ApplicationDbContext db, GenerationQueue queue, SchedulingInputs inputs,
    AccountService accounts, AvailabilityService availability, TimeProvider clock)
{
    public async Task<GenerationResponse> Save(Guid id, ClaimsPrincipal actor, CancellationToken ct)
    {
        var job = queue.Get(id);
        await job.Gate.WaitAsync(ct);
        try
        {
            queue.Get(id); // Recheck expiry after waiting behind another save/cancel.
            if (job.Response.State != "READY" || job.Problem is null) throw new ApiException("PREVIEW_NOT_READY", 409);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            // All scheduling saves serialize across instances, including adjacent-month drafts.
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(71004)", ct);
            var first = new DateOnly(job.Year, job.Month, 1);
            foreach (var month in new[] { first.AddMonths(-1), first, first.AddMonths(1) })
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({71003}, {month.Year * 12 + month.Month})", ct);
                await db.Set<SchedulePeriod>().FromSqlInterpolated($"SELECT * FROM \"SchedulePeriod\" WHERE \"Year\" = {month.Year} AND \"Month\" = {month.Month} FOR UPDATE").LoadAsync(ct);
            }
            await accounts.Lock();
            if (!Guid.TryParse(actor.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId)) throw new ApiException("UNAUTHENTICATED", 401);
            var admin = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == actorId, ct);
            if (admin is null || !admin.IsActive || admin.SecurityStamp != actor.FindFirstValue("AspNet.Identity.SecurityStamp")) throw new ApiException("UNAUTHENTICATED", 401);
            if (admin.MustChangePassword) throw new ApiException("PASSWORD_CHANGE_REQUIRED", 403);
            if (!await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id where ur.UserId == actorId && r.NormalizedName == "ADMIN" select ur).AnyAsync(ct)) throw new ApiException("FORBIDDEN", 403);
            foreach (var userId in await db.Users.OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync(ct)) await availability.LockEmployee(userId);
            var current = await inputs.Read(job.Year, job.Month, ct);
            if (current.Fingerprint != job.Problem.Fingerprint) throw new ApiException("STALE_PREVIEW", 409);
            Validate(current, job.Response.Cells);
            var period = await db.Set<SchedulePeriod>().SingleOrDefaultAsync(p => p.Year == job.Year && p.Month == job.Month, ct);
            if (period is null) { period = new SchedulePeriod { Year = job.Year, Month = job.Month }; db.Add(period); }
            if (period.Status != ScheduleStatus.DRAFT) throw new ApiException("PERIOD_PUBLISHED", 409);
            var summary = job.Response.Summary!;
            db.Add(new SchedulingRun { Id = job.Id, Year = job.Year, Month = job.Month, SavedAt = clock.GetUtcNow().UtcDateTime,
                SavedByUserId = actorId, InputFingerprint = current.Fingerprint, SolverStatus = job.Response.SolverStatus!, ProvenObjectives = job.Response.ProvenObjectives,
                Required = summary.Required, Filled = summary.Filled, Missing = summary.Missing, UnderstaffedCells = summary.UnderstaffedCells, FullyStaffedCells = summary.FullyStaffedCells });
            var replaceIds = current.Work.Where(w => w.Replaceable).Select(w => w.AssignmentId).ToArray();
            db.RemoveRange(await db.Set<ShiftAssignment>().Where(a => replaceIds.Contains(a.Id)).ToArrayAsync(ct));
            // Flush deletes before replacements to preserve the canonical membership unique index.
            await db.SaveChangesAsync(ct);
            var end = first.AddMonths(1);
            var shifts = await db.Set<Shift>().Where(s => s.ShiftDate >= first && s.ShiftDate < end).ToDictionaryAsync(s => (s.ShiftDate, s.ShiftType), ct);
            var cells = await db.Set<SchedulingCell>().Where(c => c.Date >= first && c.Date < end).ToDictionaryAsync(c => (c.ShiftTypeDefinitionId, c.Date), ct);
            foreach (var generated in job.Response.Cells)
            {
                var definition = current.Cells.Single(c => c.DefinitionId == generated.DefinitionId && c.Date == generated.Date);
                if (!cells.TryGetValue((generated.DefinitionId, generated.Date), out var cell))
                { cell = new SchedulingCell { ShiftTypeDefinitionId = generated.DefinitionId, Date = generated.Date, SchedulePeriodId = period.Id }; db.Add(cell); }
                cell.StartsAt = generated.StartsAt; cell.EndsAt = generated.EndsAt;
                cell.RequiredEmployees = generated.RequiredEmployees; cell.ConfigurationVersion = definition.Version; cell.SchedulingRunId = job.Id;
                if (!shifts.TryGetValue((cell.Date, definition.AnchorType), out var shift))
                {
                    var times = ShiftClock.Boundaries(cell.Date, definition.AnchorType);
                    shift = new Shift { SchedulePeriodId = period.Id, ShiftDate = cell.Date, ShiftType = definition.AnchorType, StartsAt = times.StartsAt, EndsAt = times.EndsAt };
                    shifts.Add((cell.Date, definition.AnchorType), shift); db.Add(shift);
                }
                foreach (var employee in generated.Employees.Where(e => !e.Protected))
                    db.Add(new ShiftAssignment { ShiftId = shift.Id, UserId = employee.UserId, SchedulingCellId = cell.Id, GeneratedByRunId = job.Id });
            }
            period.UpdatedAt = clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            job.Response = job.Response with { State = "SAVED" };
            return job.Response;
        }
        finally { job.Gate.Release(); }
    }

    public static void Validate(SchedulingProblem p, GeneratedCell[] result)
    {
        if (result.Length != p.Cells.Length || result.DistinctBy(c => (c.DefinitionId, c.Date)).Count() != result.Length) throw new ApiException("STALE_PREVIEW", 409);
        var proposed = new List<(Guid User, CandidateCell Cell)>();
        foreach (var c in p.Cells)
        {
            var output = result.SingleOrDefault(x => x.DefinitionId == c.DefinitionId && x.Date == c.Date);
            if (output is null || output.StartsAt != c.Start || output.EndsAt != c.End || output.RequiredEmployees != c.Required
                || output.Employees.Length > c.Required || output.Employees.DistinctBy(e => e.UserId).Count() != output.Employees.Length) throw new ApiException("STALE_PREVIEW", 409);
            var fixedMembers = p.Work.Where(w => !w.Replaceable && w.DefinitionId == c.DefinitionId && w.Date == c.Date).ToArray();
            if (!fixedMembers.Select(w => w.UserId).ToHashSet().SetEquals(output.Employees.Where(e => e.Protected).Select(e => e.UserId))) throw new ApiException("STALE_PREVIEW", 409);
            foreach (var e in output.Employees)
            {
                var employee = p.Employees.SingleOrDefault(x => x.Id == e.UserId);
                if (employee is null || (c.RequiresShiftLeadQualification && !employee.IsShiftLeadQualified) || p.Preferences.Any(b => b.UserId == e.UserId && b.State == PreferenceType.BLOCKED && SchedulingProblem.OverlapsAnchor(c, b))) throw new ApiException("STALE_PREVIEW", 409);
                if (e.Protected)
                {
                    var original = fixedMembers.Single(w => w.UserId == e.UserId);
                    if (original.Start != c.Start || original.End != c.End || original.Lead != e.IsShiftLead
                        || p.Work.Any(w => !w.Replaceable && w.UserId == e.UserId && w.AssignmentId != original.AssignmentId
                            && SchedulingProblem.Conflicts(c.Start, c.End, w.Start, w.End))) throw new ApiException("STALE_PREVIEW", 409);
                }
                else if (!p.Eligible(employee, c) || e.IsShiftLead) throw new ApiException("STALE_PREVIEW", 409);
                proposed.Add((e.UserId, c));
            }
        }
        foreach (var group in proposed.GroupBy(x => x.User))
        {
            if (group.GroupBy(x => (x.Cell.Date, x.Cell.AnchorType)).Any(g => g.Count() > 1)) throw new ApiException("STALE_PREVIEW", 409);
            var work = group.OrderBy(x => x.Cell.Start).ToArray();
            for (var i = 1; i < work.Length; i++)
                if (SchedulingProblem.Conflicts(work[i - 1].Cell.Start, work[i - 1].Cell.End, work[i].Cell.Start, work[i].Cell.End)) throw new ApiException("STALE_PREVIEW", 409);
        }
    }
}
