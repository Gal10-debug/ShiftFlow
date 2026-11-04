using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Domain;
namespace ShiftManager.Services;

public static class PublicationAccess
{
    public static IQueryable<SchedulePublication> Latest(ApplicationDbContext db) => db.Set<SchedulePublication>().AsNoTracking()
        .Where(p => !db.Set<SchedulePublication>().Any(n => n.SchedulePeriodId == p.SchedulePeriodId && n.VersionNumber > p.VersionNumber));

    public static IQueryable<SchedulePublication> Visible(ApplicationDbContext db) => db.Set<SchedulePublication>().AsNoTracking()
        .Where(p => (p.PublicationType == "PRELIMINARY" || p.PublicationType == "FINAL")
            && !db.Set<PublicationControl>().Any(c => c.PublicationId == p.Id && (c.Hidden || c.DeletedAt != null))
            && Latest(db).Any(active => active.SchedulePeriodId == p.SchedulePeriodId
                && !db.Set<PublicationControl>().Any(c => c.PublicationId == active.Id && (c.Hidden || c.DeletedAt != null))));
}
