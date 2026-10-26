namespace ShiftManager.Domain;

// Board ranges are independent of the canonical DAY/NIGHT availability anchors.
public enum ShiftTimeRange { DAY, NIGHT, H06_09, H07_13, H07_15, H15_23, H23_07 }

public static class ShiftTimeRanges
{
    public static bool TryParse(string? value, out ShiftTimeRange range) =>
        Enum.TryParse(value, out range) && Enum.IsDefined(range) && range.ToString() == value;
    public static ShiftTimeRange Effective(ShiftTypeDefinition row) => row.TimeRange ?? (row.Period == ShiftType.DAY ? ShiftTimeRange.DAY : ShiftTimeRange.NIGHT);
    // Retained for compatibility, not sufficient for checking hard blocks on custom ranges.
    public static ShiftType Classification(ShiftTimeRange range) => range is ShiftTimeRange.NIGHT or ShiftTimeRange.H23_07 ? ShiftType.NIGHT : ShiftType.DAY;
    public static (DateTime StartsAt, DateTime EndsAt) Boundaries(DateOnly date, ShiftTimeRange range)
    {
        var (start, end) = range switch
        {
            ShiftTimeRange.DAY => (7, 19), ShiftTimeRange.NIGHT => (19, 7),
            ShiftTimeRange.H06_09 => (6, 9), ShiftTimeRange.H07_13 => (7, 13),
            ShiftTimeRange.H07_15 => (7, 15), ShiftTimeRange.H15_23 => (15, 23),
            ShiftTimeRange.H23_07 => (23, 7), _ => throw new ArgumentOutOfRangeException(nameof(range))
        };
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Jerusalem");
        var endDate = end <= start ? date.AddDays(1) : date;
        return (TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(new TimeOnly(start, 0)), zone),
            TimeZoneInfo.ConvertTimeToUtc(endDate.ToDateTime(new TimeOnly(end, 0)), zone));
    }
    // Every overlapping canonical anchor must be checked by Phase 4B, including the
    // previous night's hard blocks for a 06:00 start. Touching endpoints do not overlap.
    public static (DateOnly Date, ShiftType Period)[] AvailabilityAnchors(DateOnly date, ShiftTimeRange range)
    {
        var interval = Boundaries(date, range);
        return Enumerable.Range(-1, 3).SelectMany(offset => Enum.GetValues<ShiftType>().Select(period => (Date: date.AddDays(offset), Period: period)))
            .Where(anchor => { var times = ShiftClock.Boundaries(anchor.Date, anchor.Period); return times.StartsAt < interval.EndsAt && times.EndsAt > interval.StartsAt; }).ToArray();
    }
}
