using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using ShiftManager.Api;
using ShiftManager.Data;
using ShiftManager.Domain;
using ShiftManager.Services;

namespace ShiftManager.Development;

public sealed record DemoScenarioCase(DemoAccount Account, string Category, string Workflow, AbsencePattern Pattern);
public sealed record DemoScenarioPreview(string Scenario, string Database, string Token, DemoScenarioItem[] Items,
    int Unaffected, string[] UnaffectedAccounts, int PlannedRequests, int PlannedDecisions, string AssignmentConflict);
public sealed record DemoScenarioItem(string Login, int Team, bool Qualified, string Category, string Workflow,
    AbsencePattern Pattern, string Action, int PlannedConflictRecords = 0)
{
    public int PlannedWeekdayRecords => Action == "ADD" ? Pattern.Weekdays.Length * (Workflow is "MODIFY" or "CANCEL" ? 2 : 1) : 0;
    public AbsencePattern? Modification => Workflow == "MODIFY" ? Pattern with { StartTime = Pattern.StartTime!.Value.AddHours(1) } : null;
}

// Additive only: no seed, migration, scheduler, publication or availability mutation is invoked here.
public static class DemoScenario
{
    public const string Version = "realistic-absences-v1";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    public static string Identity(DateOnly month, int seed) => $"{Version}/{month:yyyy-MM}/{seed}";
    public static DemoScenarioCase[] Plan(DateOnly month, int seed, IEnumerable<DemoAccount> eligible)
    {
        // Team round robin, with a seeded stable shuffle within each team. Selection includes
        // both qualification levels, independently of organizational leadership.
        string Rank(DemoAccount a) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{Identity(month, seed)}/{a.Id}")));
        var teams = eligible.GroupBy(a => a.Team).OrderBy(g => g.Key)
            .Select(g => g.OrderBy(Rank).ToArray()).ToArray();
        var people = Enumerable.Range(0, teams.Max(t => t.Length)).SelectMany(i => teams.Where(t => i < t.Length).Select(t => t[i])).Take(33).ToArray();
        if (people.Length != 33 || teams.Length != 4) throw new InvalidOperationException("נדרשים 33 עובדים כשירים מארבעה צוותים.");
        var first = month.AddMonths(-1); var last = month.AddMonths(1).AddDays(-1);
        var result = new List<DemoScenarioCase>();
        for (var i = 0; i < people.Length; i++)
        {
            var category = i < 4 ? "RESERVE" : i < 9 ? "VACATION" : i < 17 ? "STUDIES" : i < 23 ? "RECURRING_OTHER" : "SHORT";
            var workflow = i is 12 or 19 or 26 ? "PENDING" : i is 13 or 20 or 27 ? "REJECTED" : i == 28 ? "MODIFY" : i == 29 ? "CANCEL" : "APPROVED";
            var recurring = i is >= 9 and < 23;
            var start = first; var end = last;
            if (i < 4)
            {
                (start, end) = i switch { 0 => (first, month.AddDays(-1)), 1 => (month.AddDays(6), month.AddDays(19)),
                    2 => (first.AddDays(14), first.AddDays(20)), _ => (month.AddDays(-4), month.AddDays(4)) };
            }
            else if (i < 9)
            {
                start = (i % 2 == 0 ? month : first).AddDays(3 + (i - 4) * 3);
                end = start.AddDays(new[] { 3, 5, 7, 10, 4 }[i - 4] - 1);
            }
            else if (!recurring) { start = (i % 2 == 0 ? first : month).AddDays(2 + i - 23); end = start; }
            var full = i < 9 || i == 10 || i == 24;
            var weekdays = recurring ? i == 11 ? new[] { 1, 3 } : new[] { (i - 9) % 7 } : Array.Empty<int>();
            var from = full ? (TimeOnly?)null : i % 3 == 0 ? new(18, 0) : new(8 + i % 3, 0);
            var through = full ? (TimeOnly?)null : i % 3 == 0 ? new(22, 0) : new(16 + i % 2, 0);
            result.Add(new(people[i], category, workflow, new(category is "RECURRING_OTHER" or "SHORT" ? "OTHER" : category,
                start, end, recurring, weekdays, full, from, through, $"[{Identity(month, seed)}/{people[i].Login}] נתוני בדיקה סינתטיים")));
        }
        return result.ToArray();
    }

    public static async Task<DemoScenarioPreview> Run(IServiceProvider services, string environment, string connection, string[] args)
    {
        var options = new Dictionary<string, string>();
        for (var i = 1; i < args.Length; i += 2)
            if (i + 1 == args.Length || args[i] is not ("--month" or "--demo-month" or "--seed" or "--approve") || !options.TryAdd(args[i], args[i + 1]))
                throw new InvalidOperationException("אפשרויות: --month YYYY-MM --demo-month YYYY-MM --seed 42 [--approve PREVIEW_TOKEN]");
        var month = DemoPlan.ParseMonth(options.GetValueOrDefault("--month", "2026-12"));
        var demoMonth = DemoPlan.ParseMonth(options.GetValueOrDefault("--demo-month", month.AddMonths(-1).ToString("yyyy-MM")));
        if (!int.TryParse(options.GetValueOrDefault("--seed", "42"), out var seed)) throw new InvalidOperationException("זרע לא תקין.");
        var apply = args[0] == "apply-demo-scenario";
        if (!apply && args[0] != "preview-demo-scenario") throw new InvalidOperationException("פקודה לא מוכרת.");
        DemoCli.ValidateTarget(environment, connection, demoMonth, seed);
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        // Also verify DI's actual target: callers cannot pass a safe guard connection and
        // accidentally use a normal development DbContext for the service writes.
        var actual = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString()!);
        var target = new NpgsqlConnectionStringBuilder(connection);
        if (actual.ConnectionString != target.ConnectionString) throw new InvalidOperationException("חיבור השירות אינו תואם למסד הדמו.");
        await db.Database.OpenConnectionAsync();
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var tx = await db.Database.BeginTransactionAsync();
        if (!apply) await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY");
        // Same lock order as absence decisions and availability. One transaction spans
        // snapshot, service validation and verification; every failure rolls back the batch.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(781520)");
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(71004)");
        foreach (var p in await db.Set<SchedulePeriod>().AsNoTracking().OrderBy(p => p.Year).ThenBy(p => p.Month).ToArrayAsync())
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({71003}, {p.Year * 12 + p.Month})");
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(4218072)");
        async Task<object?> Scalar(string sql)
        {
            await using var cmd = new NpgsqlCommand(sql, conn, (NpgsqlTransaction)tx.GetDbTransaction());
            return await cmd.ExecuteScalarAsync();
        }
        if (!Equals(await Scalar("SELECT current_database()"), target.Database)
            || await Scalar("SELECT to_regclass('public.shiftflow_demo_owner') IS NOT NULL") is not true)
            throw new InvalidOperationException("מסד הדמו לא זוהה; לא בוצעו שינויים.");
        if (!Equals(await Scalar("SELECT version FROM public.shiftflow_demo_owner WHERE id=1"), $"{DemoPlan.Version}/{demoMonth:yyyy-MM}/{seed}")
            || await Scalar("SELECT complete FROM public.shiftflow_demo_owner WHERE id=1") is not true)
            throw new InvalidOperationException("סמן הבעלות של הדמו אינו תואם.");
        var users = await db.Users.AsNoTracking().ToArrayAsync();
        var expected = DemoPlan.Accounts(seed);
        if (users.Length != 60 || expected.Any(a => !users.Any(u => u.Id == a.Id && u.UserName == a.Login)))
            throw new InvalidOperationException("נדרשים 60 חשבונות הדמו המקוריים; חשבונות אחרים נשמרו.");
        var teams = await db.Set<OrganizationalTeam>().AsNoTracking().ToArrayAsync();
        var members = await db.Set<TeamMembership>().AsNoTracking().ToArrayAsync();
        var roles = await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id select new { ur.UserId, r.Name }).ToArrayAsync();
        if (teams.Length != 4 || members.Length != 60 || teams.Count(t => t.LeaderUserId.HasValue) != 4 || teams.Count(t => t.DeputyUserId.HasValue) != 4)
            throw new InvalidOperationException("מבנה צוותי הדמו אינו תקין.");
        var eligible = expected.Where(a => users.Single(u => u.Id == a.Id) is { IsActive: true, MustChangePassword: false }
            && !teams.Any(t => t.LeaderUserId == a.Id) && roles.Any(r => r.UserId == a.Id && r.Name == "EMPLOYEE"))
            .Select(a => a with { Team = teams.Single(t => t.Id == members.Single(m => m.UserId == a.Id).TeamId).Slot,
                Qualified = users.Single(u => u.Id == a.Id).IsShiftLeadQualified }).ToArray();
        var plan = Plan(month, seed, eligible);
        var existing = await db.Set<AbsenceRequest>().AsNoTracking().Include(r => r.Weekdays).ToArrayAsync();
        var work = await db.Set<ShiftAssignment>().AsNoTracking().Include(a => a.Cell).Include(a => a.Shift).ThenInclude(s => s.SchedulePeriod).ToArrayAsync();
        // Exercise a warning against existing work when possible, without creating or
        // moving assignments. Swap distinct scenario identities and target that weekday.
        // Ignore our own markers so the chosen conflict remains stable on rerun.
        var pendingIndex = Array.FindIndex(plan, c => c.Workflow == "PENDING");
        var conflictWork = work.Where(a => eligible.Any(e => e.Id == a.UserId)
            && a.Shift.ShiftDate >= month.AddMonths(-1) && a.Shift.ShiftDate < month.AddMonths(1))
            .Where(a => !existing.Any(r => r.UserId == a.UserId && !(r.Explanation ?? "").StartsWith($"[{Identity(month, seed)}/") && r.StartDate <= month.AddMonths(1).AddDays(-1) && (r.EndDate == null || r.EndDate >= month.AddMonths(-1))))
            .OrderBy(a => a.Shift.ShiftDate).ThenBy(a => a.Id).FirstOrDefault();
        if (conflictWork is not null)
        {
            var selected = eligible.Single(e => e.Id == conflictWork.UserId);
            var previous = plan[pendingIndex].Account;
            var swapped = Array.FindIndex(plan, c => c.Account.Id == selected.Id);
            if (swapped >= 0) plan[swapped] = plan[swapped] with { Account = previous,
                Pattern = plan[swapped].Pattern with { Explanation = $"[{Identity(month, seed)}/{previous.Login}] נתוני בדיקה סינתטיים" } };
            plan[pendingIndex] = plan[pendingIndex] with { Account = selected, Pattern = new("STUDIES", month.AddMonths(-1),
                month.AddMonths(1).AddDays(-1), true, [(int)conflictWork.Shift.ShiftDate.DayOfWeek], true, null, null, $"[{Identity(month, seed)}/{selected.Login}] נתוני בדיקה סינתטיים") };
        }
        var publications = (await db.Set<SchedulePublication>().AsNoTracking()
            .Where(p => p.PublicationType == "PRELIMINARY" || p.PublicationType == "FINAL").ToArrayAsync())
            .GroupBy(p => p.SchedulePeriodId).Select(g => JsonSerializer.Deserialize<ScheduleSnapshot>(g.MaxBy(p => p.VersionNumber)!.Snapshot)!)
            .ToArray();
        int PublishedConflicts(DemoScenarioCase c, AbsencePattern p)
        {
            var rule = Rule(c with { Pattern = p });
            return publications.Sum(snapshot => snapshot.Cells.Where(cell => AbsenceRules.Overlaps(rule, cell.StartsAt, cell.EndsAt))
                .Sum(cell => cell.Employees.Count(e => e.UserId == c.Account.Id))
                + snapshot.LegacyAssignments.Count(a => a.UserId == c.Account.Id && AbsenceRules.Overlaps(rule, a.StartsAt, a.EndsAt)));
        }
        var items = plan.Select(c =>
        {
            var own = existing.Where(r => r.Explanation == c.Pattern.Explanation).ToArray();
            var other = existing.Any(r => r.UserId == c.Account.Id && r.Explanation != c.Pattern.Explanation
                && r.StartDate <= c.Pattern.EndDate && (r.EndDate == null || r.EndDate >= c.Pattern.StartDate));
            var rule = Rule(c);
            var collision = work.Any(a => a.UserId == c.Account.Id && ScheduleReviewService.Editable(a.Shift.SchedulePeriod.Status) && AbsenceRules.Overlaps(rule, a.Cell?.StartsAt ?? a.Shift.StartsAt, a.Cell?.EndsAt ?? a.Shift.EndsAt));
            var action = own.Length > 0 ? "EXISTS" : other ? "SKIP_EXISTING_ABSENCE" : collision && c.Workflow is not ("PENDING" or "REJECTED") ? "SKIP_ASSIGNMENT" : "ADD";
            return new DemoScenarioItem(c.Account.Login, c.Account.Team, c.Account.Qualified, c.Category, c.Workflow, c.Pattern, action,
                action != "ADD" || c.Workflow is "PENDING" or "REJECTED" ? 0 : PublishedConflicts(c, c.Pattern)
                    + (c.Workflow == "MODIFY" ? PublishedConflicts(c, c.Pattern with { StartTime = c.Pattern.StartTime!.Value.AddHours(1) }) : 0));
        }).ToArray();
        var hash = await DemoCli.Fingerprint(conn);
        var token = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Identity(month, seed) + hash + JsonSerializer.Serialize(items))));
        var added = items.Where(i => i.Action == "ADD").ToArray();
        var conflictCase = plan.Any(c => c.Workflow == "PENDING" && work.Any(a => a.UserId == c.Account.Id && AbsenceRules.Overlaps(Rule(c), a.Cell?.StartsAt ?? a.Shift.StartsAt, a.Cell?.EndsAt ?? a.Shift.EndsAt)));
        var preview = new DemoScenarioPreview(Identity(month, seed), target.Database!, token, items, 27, expected.Where(a => !plan.Any(c => c.Account.Id == a.Id)).Select(a => a.Login).ToArray(),
            added.Sum(i => i.Workflow is "MODIFY" or "CANCEL" ? 2 : 1), added.Sum(i => i.Workflow == "PENDING" ? 0 : i.Workflow is "MODIFY" or "CANCEL" ? 2 : 1),
            conflictCase ? "בקשה ממתינה מתנגשת בשיבוץ קיים; השיבוץ יישמר" : "לא נמצא מקרה התנגשות בטוח בתוכנית; לא ייווצר שיבוץ לצורך הבדיקה");
        if (apply)
        {
            if (options.GetValueOrDefault("--approve") != token) throw new InvalidOperationException("נדרש אישור מפורש עם אסימון תצוגה מקדימה עדכני (--approve). לא בוצעו שינויים.");
            var protectedBefore = await DemoCli.Fingerprint(conn, true);
            var beforeRequests = JsonSerializer.Serialize(existing.Select(Snapshot).OrderBy(x => x));
            var absence = scope.ServiceProvider.GetRequiredService<AbsenceService>();
            foreach (var c in plan.Where(c => items.Single(i => i.Login == c.Account.Login).Action == "ADD"))
            {
                var employee = DemoCli.Principal(users.Single(u => u.Id == c.Account.Id), roles.Where(r => r.UserId == c.Account.Id).Select(r => r.Name!).ToArray());
                var team = teams.Single(t => t.Slot == c.Account.Team);
                var approverId = c.Account.Index % 2 == 0 ? team.LeaderUserId!.Value : team.DeputyUserId!.Value;
                var approver = DemoCli.Principal(users.Single(u => u.Id == approverId), roles.Where(r => r.UserId == approverId).Select(r => r.Name!).ToArray());
                var request = await absence.Submit(c.Pattern, employee);
                async Task<AbsenceResponse> Approve(AbsenceResponse r)
                {
                    var report = await absence.Conflicts(r.Id, approver);
                    if (report.Items.Any(i => i.Removable || i.RemovalBlock is not null)) throw new InvalidOperationException("התנגשות בשיבוץ; הפעולה בוטלה ללא מחיקה.");
                    return await absence.Decide(r.Id, new(r.Version, "APPROVED", null, report.Token, false), approver);
                }
                if (c.Workflow == "PENDING") continue;
                if (c.Workflow == "REJECTED") { await absence.Decide(request.Id, new(request.Version, "REJECTED", "בדיקת דמו: הבקשה נדחתה לצורך תרגול תהליך האישור"), approver); continue; }
                request = await Approve(request);
                if (c.Workflow is "MODIFY" or "CANCEL")
                {
                    // The modification narrows the original hours, never expanding into draft work.
                    var changed = c.Pattern with { StartTime = c.Pattern.StartTime!.Value.AddHours(1) };
                    var child = await absence.Change(request.Id, new(request.Version, c.Workflow, c.Workflow == "MODIFY" ? changed : null), employee);
                    await Approve(child);
                }
            }
            db.ChangeTracker.Clear();
            if (protectedBefore != await DemoCli.Fingerprint(conn, true)
                || beforeRequests != JsonSerializer.Serialize((await db.Set<AbsenceRequest>().AsNoTracking().Include(r => r.Weekdays).Where(r => existing.Select(e => e.Id).Contains(r.Id)).ToArrayAsync()).Select(Snapshot).OrderBy(x => x)))
                throw new InvalidOperationException("אימות שימור נתונים נכשל; כל התוספות בוטלו.");
        }
        await tx.CommitAsync();
        var output = Path.Combine(Directory.GetCurrentDirectory(), ".demo", target.Database!);
        Directory.CreateDirectory(output);
        var path = Path.Combine(output, apply ? "scenario-application.json" : "scenario-preview.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(preview, Json));
        var markdown = new StringBuilder($"# תרחיש היעדרויות דמו — {month:yyyy-MM}\n\n{(apply ? "הפעולה הושלמה" : "תצוגה מקדימה בלבד; לא נכתבו נתונים למסד")}\n\n");
        markdown.AppendLine($"מסד: `{target.Database}`; תרחיש: `{preview.Scenario}`.\n");
        markdown.AppendLine($"להוספה: {added.Length} חשבונות, {preview.PlannedRequests} בקשות ו-{preview.PlannedDecisions} החלטות.\n");
        markdown.AppendLine($"בנוסף: {items.Sum(i => i.PlannedWeekdayRecords)} רשומות ימי חזרה ו-{items.Sum(i => i.PlannedConflictRecords)} רשומות התנגשות עם פרסום קיים; הפרסומים יישמרו.\n");
        markdown.AppendLine("| קבוצה | יעד | להוספה | דילוג | חשבונות |\n|---|---:|---:|---:|---|");
        foreach (var (key, label) in new[] { ("RESERVE", "מילואים"), ("VACATION", "חופשה"), ("STUDIES", "לימודים"), ("RECURRING_OTHER", "מגבלות חוזרות אחרות"), ("SHORT", "מגבלות קצרות") })
        {
            var group = items.Where(i => i.Category == key).ToArray();
            markdown.AppendLine($"| {label} | {group.Length} | {group.Count(i => i.Action == "ADD")} | {group.Count(i => i.Action.StartsWith("SKIP"))} | {string.Join(", ", group.Select(i => i.Login))} |");
        }
        markdown.AppendLine($"\nללא תרחיש נוסף: {string.Join(", ", preview.UnaffectedAccounts)}\n");
        markdown.AppendLine($"{preview.AssignmentConflict}\n");
        markdown.AppendLine("פרטי התאריכים, השעות, החזרתיות, הצוותים, הכשירויות, ההחלטות וסיבות הדילוג מופיעים בקובץ JSON הסמוך. בקשת שינוי מאושרת מצמצמת את שעות הבקשה המקורית בשעה אחת; בקשת ביטול מאושרת מפסיקה לחסום שיבוץ. דילוגים אינם משנים נתונים קיימים.");
        await File.WriteAllTextAsync(Path.ChangeExtension(path, ".md"), markdown.ToString());
        Console.WriteLine($"{(apply ? "התרחיש הוחל" : "תצוגה מקדימה בלבד — לא נכתבו נתונים")}: {preview.Scenario}\nמילואים: 4; חופשה: 5; לימודים: 8; מגבלות חוזרות: 6; מגבלות קצרות: 10; ללא תרחיש נוסף: 27.\nחשבונות להוספה: {added.Length}; בקשות: {preview.PlannedRequests}; החלטות: {preview.PlannedDecisions}; דילוגים: {items.Count(i => i.Action.StartsWith("SKIP"))}.\n{preview.AssignmentConflict}\nדוח ללא סיסמאות: {path}\nאסימון אישור: {token}");
        return preview;
    }
    private static AbsenceRule Rule(DemoScenarioCase c) => new(Guid.Empty, c.Account.Id, c.Pattern.StartDate, c.Pattern.EndDate,
        c.Pattern.Recurring, c.Pattern.Weekdays, c.Pattern.FullDay, c.Pattern.StartTime, c.Pattern.EndTime);
    private static string Snapshot(AbsenceRequest r) => JsonSerializer.Serialize(new { r.Id, r.UserId, r.Kind, r.Status, r.Reason, r.Explanation,
        r.StartDate, r.EndDate, r.Recurring, r.FullDay, r.StartTime, r.EndTime, r.ReplacesId, r.ReplacesVersion, r.Version, r.CreatedAt, r.UpdatedAt,
        Weekdays = r.Weekdays.Select(w => w.Day).Order().ToArray() });
}
