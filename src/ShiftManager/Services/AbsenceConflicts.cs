using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Api;
using ShiftManager.Domain;
using ShiftManager.Services.AutomaticScheduling;
namespace ShiftManager.Services;

public sealed partial class AbsenceService
{
    private async Task<AbsenceConflictReport> FindConflicts(AbsenceRequest request)
    {
        var result = new List<AbsenceConflictItem>();
        var rule = AbsenceRule.From(request);
        if (request.Kind != "CANCEL")
        {
            var work = await db.Set<ShiftAssignment>().AsNoTracking().Include(a => a.Cell).ThenInclude(c => c!.Definition)
                .Include(a => a.Shift).ThenInclude(s => s.SchedulePeriod).Include(a => a.Tasks)
                .Where(a => a.UserId == request.UserId).ToArrayAsync();
            DateTime Start(ShiftAssignment a) => a.Cell?.StartsAt ?? a.Shift.StartsAt;
            DateTime End(ShiftAssignment a) => a.Cell?.EndsAt ?? a.Shift.EndsAt;
            var overlapping = work.Where(a => AbsenceRules.Overlaps(rule, Start(a), End(a))).ToArray();
            bool Paired(ShiftAssignment a, ShiftAssignment b) => a.Cell?.Definition.Semantic == SchedulingRules.OnCall
                && b.Cell?.ShiftTypeDefinitionId == a.Cell.ShiftTypeDefinitionId
                && SchedulingRules.Friday(a.Cell.Date) == SchedulingRules.Friday(b.Cell.Date);
            // Pair mates are part of the explicit removal proposal, even if only one day overlaps.
            foreach (var a in work.Where(a => ScheduleReviewService.Editable(a.Shift.SchedulePeriod.Status)
                && (overlapping.Any(b => b.Id == a.Id) || overlapping.Any(b => Paired(a, b)))))
            {
                var p = a.Shift.SchedulePeriod;
                var blockedPair = work.Any(b => Paired(a, b) && !ScheduleReviewService.Editable(b.Shift.SchedulePeriod.Status));
                result.Add(new(a.Id, p.Id, p.Year, p.Month, a.Cell?.Date ?? a.Shift.ShiftDate, Start(a), End(a),
                    a.Cell?.Definition.Name ?? "משמרת קודמת", "DRAFT", null, null, true,
                    a.Tasks.Count > 0 ? "ASSIGNMENT_HAS_TASKS" : blockedPair ? "ON_CALL_ADJACENT_CONFIRMATION" : null));
            }
            // Only the active published version needs correction. Earlier JSON remains history.
            var publications = (await db.Set<SchedulePublication>().AsNoTracking().Include(p => p.SchedulePeriod)
                .Where(p => p.PublicationType == "PRELIMINARY" || p.PublicationType == "FINAL").ToArrayAsync())
                .GroupBy(p => p.SchedulePeriodId).Select(g => g.MaxBy(p => p.VersionNumber)!);
            var deleted = await db.Set<PublicationControl>().Where(c => c.DeletedAt != null).Select(c => c.PublicationId).ToArrayAsync();
            foreach (var publication in publications.Where(p => !deleted.Contains(p.Id)))
            {
                var snapshot = JsonSerializer.Deserialize<ScheduleSnapshot>(publication.Snapshot);
                if (snapshot is null) continue;
                var p = publication.SchedulePeriod;
                foreach (var cell in snapshot.Cells.Where(c => c.Employees.Any(e => e.UserId == request.UserId) && AbsenceRules.Overlaps(rule, c.StartsAt, c.EndsAt)))
                    foreach (var member in cell.Employees.Where(e => e.UserId == request.UserId))
                        result.Add(new(member.AssignmentId, p.Id, p.Year, p.Month, cell.Date, cell.StartsAt, cell.EndsAt,
                            cell.Name, publication.PublicationType, publication.Id, publication.VersionNumber, false));
                foreach (var a in snapshot.LegacyAssignments.Where(a => a.UserId == request.UserId && AbsenceRules.Overlaps(rule, a.StartsAt, a.EndsAt)))
                    result.Add(new(a.AssignmentId, p.Id, p.Year, p.Month, a.Date, a.StartsAt, a.EndsAt,
                        "משמרת קודמת", publication.PublicationType, publication.Id, publication.VersionNumber, false));
            }
        }
        var items = result.OrderBy(c => c.Date).ThenBy(c => c.AssignmentId).ThenBy(c => c.PublicationId).ToArray();
        var token = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { request.Id, request.Version, request.ReplacesVersion, items })));
        return new(token, items);
    }
    private async Task ApplyConflicts(AbsenceRequest request, AbsenceConflictReport report, Guid actor)
    {
        var ids = report.Items.Where(i => i.Removable).Select(i => i.AssignmentId).Distinct().ToArray();
        var removed = await db.Set<ShiftAssignment>().Include(a => a.Shift).Where(a => ids.Contains(a.Id)).ToArrayAsync();
        foreach (var group in removed.GroupBy(a => a.Shift.SchedulePeriodId))
        {
            var period = await db.Set<SchedulePeriod>().SingleAsync(p => p.Id == group.Key);
            period.Revision++; period.UpdatedAt = Now; period.ReviewedInputs = null;
            db.Add(new ScheduleEditAudit { SchedulePeriodId = period.Id, ActorUserId = actor, OccurredAt = Now, Operation = "ABSENCE_REMOVE",
                Before = JsonSerializer.Serialize(group.Select(a => new { a.Id, a.UserId, a.ShiftId, a.SchedulingCellId, a.Note })),
                After = JsonSerializer.Serialize(new { AbsenceRequestId = request.Id }) });
        }
        db.RemoveRange(removed);
        foreach (var c in report.Items.Where(c => c.PublicationId.HasValue))
            db.Add(new AbsenceConflict { AbsenceRequestId = request.Id, AssignmentId = c.AssignmentId, SchedulePeriodId = c.SchedulePeriodId,
                PublicationId = c.PublicationId!.Value, Date = c.Date, StartsAt = c.StartsAt, EndsAt = c.EndsAt, ShiftName = c.ShiftName });
    }
}
