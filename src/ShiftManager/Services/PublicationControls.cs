using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Api;
using ShiftManager.Domain;
namespace ShiftManager.Services;

public sealed partial class ScheduleReviewService
{
    private IQueryable<SchedulePublication> EmployeePublications() => PublicationAccess.Visible(db);

    // The newest version remains authoritative even when unavailable. Never fall back.
    private async Task<SchedulePublication> VisibleLatest(Guid periodId)
    {
        var latest = await db.Set<SchedulePublication>().AsNoTracking().Where(p => p.SchedulePeriodId == periodId)
            .OrderByDescending(p => p.VersionNumber).FirstOrDefaultAsync() ?? throw new ApiException("NOT_FOUND", 404);
        if (!await EmployeePublications().AnyAsync(p => p.Id == latest.Id)) throw new ApiException("NOT_FOUND", 404);
        return latest;
    }
    public async Task<int[]> VisibleVersions(int year, int month, ClaimsPrincipal actor)
    {
        AvailabilityService.MonthStart(year, month);
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        await Employee(actor); var period = await Period(year, month) ?? throw new ApiException("NOT_FOUND", 404);
        await VisibleLatest(period.Id);
        var result = await EmployeePublications().Where(p => p.SchedulePeriodId == period.Id).OrderByDescending(p => p.VersionNumber).Select(p => p.VersionNumber).ToArrayAsync();
        await tx.CommitAsync(); return result;
    }
    public async Task ControlPublication(int year, int month, Guid publicationId, PublicationControlRequest input, ClaimsPrincipal actor)
    {
        if (input.Operation is not ("HIDE" or "RESTORE" or "DELETE") || string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 2000)
            throw new ApiException("VALIDATION_ERROR");
        await using var tx = await db.Database.BeginTransactionAsync(); var actorId = await Lock(year, month, actor);
        var period = await Period(year, month) ?? throw new ApiException("NOT_FOUND", 404);
        if (!await db.Set<SchedulePublication>().AnyAsync(p => p.Id == publicationId && p.SchedulePeriodId == period.Id)) throw new ApiException("NOT_FOUND", 404);
        var control = await db.Set<PublicationControl>().SingleOrDefaultAsync(c => c.PublicationId == publicationId);
        if ((control?.Version ?? 0) != input.ExpectedVersion) throw new ApiException("CONFLICT", 409);
        if (control?.DeletedAt != null) throw new ApiException("PUBLICATION_UNAVAILABLE", 409);
        var before = new { PublicationId = publicationId, Hidden = control?.Hidden ?? false, DeletedAt = control?.DeletedAt };
        if (control is null) { control = new PublicationControl { PublicationId = publicationId, Version = 0 }; db.Add(control); }
        if (input.Operation == "DELETE") control.DeletedAt = Now;
        else control.Hidden = input.Operation == "HIDE";
        control.Version++; period.Revision++; period.UpdatedAt = Now;
        db.Add(new ScheduleEditAudit { SchedulePeriodId = period.Id, ActorUserId = actorId, OccurredAt = Now,
            Operation = "PUBLICATION_" + input.Operation, Before = JsonSerializer.Serialize(before),
            After = JsonSerializer.Serialize(new { PublicationId = publicationId, control.Hidden, control.DeletedAt, Reason = input.Reason.Trim() }) });
        if (input.Operation != "RESTORE")
        {
            var latest = await db.Set<SchedulePublication>().Where(p => p.SchedulePeriodId == period.Id).MaxAsync(p => p.VersionNumber);
            var affected = new[] { publicationId };
            // Hiding/deleting the authoritative version revokes every pending exchange in its month.
            var isLatest = await db.Set<SchedulePublication>().AnyAsync(p => p.Id == publicationId && p.VersionNumber == latest);
            foreach (var request in await db.Set<ShiftExchange>().Include(r => r.Assignments).Where(r =>
                (r.Status == "AWAITING_REPLACEMENT" || r.Status == "AWAITING_ADMIN") &&
                r.Assignments.Any(a => affected.Contains(a.PublicationId) || isLatest && a.Date.Year == year && a.Date.Month == month)).ToArrayAsync())
                await TransitionExchange(request, "CANCELLED", actorId, "הפרסום הוסתר או נמחק");
        }
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
}
