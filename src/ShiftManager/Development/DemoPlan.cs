using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ShiftManager.Api;
using ShiftManager.Domain;
using ShiftManager.Services;

namespace ShiftManager.Development;

public sealed record DemoAccount(int Index, Guid Id, string Login, int Team, string Position, bool Qualified);
public static class DemoPlan
{
    public const string Version = "shiftflow-demo-v1";
    public static DateOnly ParseMonth(string value) => DateOnly.TryParseExact(value + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
        DateTimeStyles.None, out var month) && month.Year is >= 3 and <= 9997 ? month : throw new InvalidOperationException("Use --month YYYY-MM (years 0003–9997).");
    public static DemoAccount[] Accounts(int seed) => Enumerable.Range(0, 60).Select(i => new DemoAccount(i,
        new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"{Version}/{seed}/{i}"))[..16]),
        $"demo.{i + 1:00}", i / 15 + 1, i % 15 == 0 ? "leader" : i % 15 == 1 ? "deputy" : "employee",
        i % 15 != 0 && i % 4 == 1)).ToArray();

    // Absolute Sunday week keys make choices stable across adjacent monthly runs.
    // Each week blocks at most three anchors, including when the week spans months.
    public static SelectionRequest[] Selections(DateOnly month, int seed, int index)
    {
        var result = new List<SelectionRequest>();
        for (var date = month; date < month.AddMonths(1); date = date.AddDays(1))
        {
            var week = AvailabilityService.WeekStart(date);
            var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{Version}/{seed}/{index}/{week:yyyy-MM-dd}"));
            var days = Enumerable.Range(0, 7).OrderBy(d => digest[d]).ThenBy(d => d).Take(index % 4).ToArray();
            var blocked = days.Contains((int)date.DayOfWeek);
            var preferred = index % 3 == 0 ? ShiftType.DAY : index % 3 == 1 ? ShiftType.NIGHT : (digest[8] % 2 == 0 ? ShiftType.DAY : ShiftType.NIGHT);
            if (blocked) result.Add(new(date, preferred.ToString(), "BLOCKED"));
            else if (index % 7 != 0 || date.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Wednesday)
                result.Add(new(date, preferred.ToString(), "PREFERRED"));
        }
        return result.ToArray();
    }
}
