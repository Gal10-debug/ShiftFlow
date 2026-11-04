using ShiftManager.Domain;
namespace ShiftManager.Services;

public sealed record AbsenceRule(Guid Id, Guid UserId, DateOnly StartDate, DateOnly? EndDate, bool Recurring,
    int[] Weekdays, bool FullDay, TimeOnly? StartTime, TimeOnly? EndTime)
{
    public static AbsenceRule From(AbsenceRequest r) => new(r.Id, r.UserId, r.StartDate, r.EndDate, r.Recurring,
        r.Weekdays.Select(d => d.Day).Order().ToArray(), r.FullDay, r.StartTime, r.EndTime);
}
public static class AbsenceRules
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Jerusalem");
    // During the autumn fold, cover both occurrences of a requested wall time.
    // During the spring gap, advance a nonexistent boundary to the first valid minute.
    public static DateTime Utc(DateTime local, bool end)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        while (Zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        if (Zone.IsAmbiguousTime(local))
        {
            var offsets = Zone.GetAmbiguousTimeOffsets(local);
            return new DateTimeOffset(local, end ? offsets.Min() : offsets.Max()).UtcDateTime;
        }
        return TimeZoneInfo.ConvertTimeToUtc(local, Zone);
    }
    public static bool Overlaps(AbsenceRule rule, DateTime start, DateTime end)
    {
        var localStart = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(start, DateTimeKind.Utc), Zone));
        var localEnd = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(end, DateTimeKind.Utc), Zone));
        var first = localStart.AddDays(-1) > rule.StartDate ? localStart.AddDays(-1) : rule.StartDate;
        var last = rule.EndDate.HasValue && rule.EndDate < localEnd ? rule.EndDate.Value : localEnd;
        for (var date = first; date <= last; date = date.AddDays(1))
        {
            if (rule.Recurring && !rule.Weekdays.Contains((int)date.DayOfWeek)) continue;
            var from = date.ToDateTime(rule.FullDay ? TimeOnly.MinValue : rule.StartTime!.Value);
            var through = rule.FullDay ? date.AddDays(1).ToDateTime(TimeOnly.MinValue)
                : (rule.EndTime <= rule.StartTime ? date.AddDays(1) : date).ToDateTime(rule.EndTime!.Value);
            if (Utc(from, false) < end && Utc(through, true) > start) return true;
        }
        return false;
    }
}
