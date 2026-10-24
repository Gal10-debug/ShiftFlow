namespace ShiftManager.Domain;
/// <summary>Resolve wall-clock boundaries separately; DST nights may span 11 or 13 elapsed hours.</summary>
public static class ShiftClock
{
    public static (DateTime StartsAt, DateTime EndsAt) Boundaries(DateOnly date, ShiftType type)
    {
        if (!Enum.IsDefined(type))
            throw new ArgumentOutOfRangeException(nameof(type));

        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Jerusalem");
        var start = date.ToDateTime(new TimeOnly(type == ShiftType.DAY ? 7 : 19, 0));
        var end = type == ShiftType.DAY ? date.ToDateTime(new TimeOnly(19, 0)) : date.AddDays(1).ToDateTime(new TimeOnly(7, 0));
        return (TimeZoneInfo.ConvertTimeToUtc(start, zone), TimeZoneInfo.ConvertTimeToUtc(end, zone));
    }
}
