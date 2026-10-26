using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Api;
using ShiftManager.Data;
using ShiftManager.Domain;

namespace ShiftManager.Services;

// Phase 4B consumes a snapshot of enabled, positive-staffing rows only. Excluded IDs are
// an explicit do-not-touch set. This contract never generates or changes assignments.
public interface IAutomaticSchedulingConfiguration
{
    Task<AutomaticSchedulingConfigurationResponse> GetConfiguration();
}

public sealed class SchedulingService(ApplicationDbContext db, AccountService accounts, TimeProvider clock)
    : IAutomaticSchedulingConfiguration
{
    public const string ZoneId = "Asia/Jerusalem";
    public static DateOnly WeekStart(DateOnly date) => AvailabilityService.WeekStart(date);
    public DateOnly CurrentWeek() => WeekStart(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(ZoneId)).DateTime));

    private static ShiftTypeResponse Response(ShiftTypeDefinition row) => new(row.Id, row.Slot, row.Name,
        row.DisplayOrder, row.Period.ToString(), row.RequiredEmployees, row.IncludeInAutomaticScheduling, row.Version, row.UpdatedAt, ShiftTimeRanges.Effective(row).ToString(), row.RequiresShiftLeadQualification);

    public async Task<ShiftTypeResponse[]> List() => (await db.Set<ShiftTypeDefinition>().AsNoTracking()
        .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Slot).ToArrayAsync()).Select(Response).ToArray();

    public async Task<ShiftTypeResponse[]> Update(UpdateShiftTypesRequest input, ClaimsPrincipal actor)
    {
        if (input.ShiftTypes is null || input.ShiftTypes.Length != 51
            || input.ShiftTypes.Any(x => x is null || string.IsNullOrWhiteSpace(x.Name) || x.Name.Length > 100
                || x.DisplayOrder is null or < 1 or > 51 || x.RequiredEmployees is null or < 0 or > 1000
                || (x.TimeRange is not null && !ShiftTimeRanges.TryParse(x.TimeRange, out _))
                || x.ExpectedVersion is null or < 1 || x.IncludeInAutomaticScheduling is null || x.Period is not ("DAY" or "NIGHT"))
            || input.ShiftTypes.Select(x => x.Id).Distinct().Count() != 51
            || input.ShiftTypes.Select(x => x.DisplayOrder).Distinct().Count() != 51)
            throw new ApiException("INVALID_SHIFT_TYPE_SETTINGS");

        await using var tx = await db.Database.BeginTransactionAsync();
        // The existing account lock also makes deactivation/role changes linearizable
        // with this infrequent administrator operation; reload claims after waiting.
        await accounts.Lock();
        if (!Guid.TryParse(actor.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId)) throw new ApiException("UNAUTHENTICATED", 401);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == actorId);
        if (user is null || !user.IsActive || user.SecurityStamp != actor.FindFirstValue("AspNet.Identity.SecurityStamp")) throw new ApiException("UNAUTHENTICATED", 401);
        if (user.MustChangePassword) throw new ApiException("PASSWORD_CHANGE_REQUIRED", 403);
        if (!await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id
                    where ur.UserId == actorId && r.NormalizedName == "ADMIN" select ur).AnyAsync()) throw new ApiException("FORBIDDEN", 403);

        var rows = await db.Set<ShiftTypeDefinition>().OrderBy(x => x.Slot).ToArrayAsync();
        if (rows.Length != 51 || !rows.Select(x => x.Id).ToHashSet().SetEquals(input.ShiftTypes.Select(x => x.Id)))
            throw new ApiException("INVALID_SHIFT_TYPE_SETTINGS");
        var proposed = input.ShiftTypes.ToDictionary(x => x.Id);
        foreach (var row in rows)
        {
            var next = proposed[row.Id];
            if (row.Version != next.ExpectedVersion) throw new ApiException("CONFLICT", 409);
            var name = next.Name.Trim();
            // Older clients omitting TimeRange retain a custom range unless they change Period.
            var range = next.TimeRange is not null ? Enum.Parse<ShiftTimeRange>(next.TimeRange)
                : next.Period == row.Period.ToString() ? ShiftTimeRanges.Effective(row) : Enum.Parse<ShiftTimeRange>(next.Period);
            if (ShiftTimeRanges.Classification(range).ToString() != next.Period) throw new ApiException("INVALID_SHIFT_TYPE_SETTINGS");
            if (row.Name == name && row.DisplayOrder == next.DisplayOrder && row.Period.ToString() == next.Period
                && ShiftTimeRanges.Effective(row) == range && row.RequiredEmployees == next.RequiredEmployees && row.IncludeInAutomaticScheduling == next.IncludeInAutomaticScheduling
                && row.RequiresShiftLeadQualification == next.RequiresShiftLeadQualification) continue;
            row.Name = name;
            row.DisplayOrder = next.DisplayOrder!.Value;
            row.Period = Enum.Parse<ShiftType>(next.Period);
            row.TimeRange = range;
            row.RequiredEmployees = next.RequiredEmployees!.Value;
            row.IncludeInAutomaticScheduling = next.IncludeInAutomaticScheduling!.Value;
            row.RequiresShiftLeadQualification = next.RequiresShiftLeadQualification;
            row.Version++;
            row.UpdatedAt = clock.GetUtcNow().UtcDateTime;
            // EF's unique-index command sorting rejects order-swap cycles before sending
            // SQL, even though PostgreSQL defers this constraint. Parameterized updates
            // use the deferred database constraint and keep explicit version checks.
            var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "ShiftTypeDefinition" SET "Name" = {row.Name}, "DisplayOrder" = {row.DisplayOrder},
                    "Period" = {next.Period}, "TimeRange" = {range.ToString()}, "RequiredEmployees" = {row.RequiredEmployees},
                    "IncludeInAutomaticScheduling" = {row.IncludeInAutomaticScheduling},
                    "RequiresShiftLeadQualification" = {row.RequiresShiftLeadQualification},
                    "Version" = {row.Version}, "UpdatedAt" = {row.UpdatedAt}
                WHERE "Id" = {row.Id} AND "Version" = {next.ExpectedVersion.Value}
                """);
            if (changed != 1) throw new ApiException("CONFLICT", 409);
        }
        await tx.CommitAsync();
        // Return this save's snapshot; a later administrator's save cannot mix versions here.
        return rows.OrderBy(x => x.DisplayOrder).Select(Response).ToArray();
    }

    public async Task<WeeklyBoardResponse> Board(DateOnly? date)
    {
        var anchor = date ?? CurrentWeek();
        if (anchor.Year < 2 || anchor.Year > 9998) throw new ApiException("VALIDATION_ERROR");
        var start = WeekStart(anchor);
        if (start.Year < 2 || start > new DateOnly(9998, 12, 25)) throw new ApiException("VALIDATION_ERROR");
        var end = start.AddDays(6);
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        var definitions = await List();
        var months = Enumerable.Range(-1, 9).Select(i => start.AddDays(i)).Select(d => (d.Year, d.Month)).Distinct().ToArray();
        var years = months.Select(x => x.Year).Distinct().ToArray();
        var monthNumbers = months.Select(x => x.Month).Distinct().ToArray();
        var periods = await db.Set<SchedulePeriod>().AsNoTracking().Where(x => years.Contains(x.Year) && monthNumbers.Contains(x.Month))
            .ToDictionaryAsync(x => (x.Year, x.Month));
        var shifts = await db.Set<Shift>().AsNoTracking().Where(x => x.ShiftDate >= start.AddDays(-1) && x.ShiftDate <= end.AddDays(1))
            .ToDictionaryAsync(x => (x.ShiftDate, x.ShiftType));
        var savedCells = await db.Set<SchedulingCell>().AsNoTracking().Include(c => c.Assignments).ThenInclude(a => a.User)
            .Include(c => c.Assignments).ThenInclude(a => a.Tasks)
            .Where(c => c.Date >= start && c.Date <= end).ToDictionaryAsync(c => (c.ShiftTypeDefinitionId, c.Date));
        var days = Enumerable.Range(0, 7).Select(i => start.AddDays(i)).Select(d => new BoardDayResponse(d, d.Year, d.Month, periods.GetValueOrDefault((d.Year, d.Month))?.Id)).ToArray();
        var rows = definitions.Select(definition => new BoardRowResponse(definition, days.Select(day =>
        {
            var range = Enum.Parse<ShiftTimeRange>(definition.TimeRange!);
            var boundaries = ShiftTimeRanges.Boundaries(day.Date, range);
            var anchors = ShiftTimeRanges.AvailabilityAnchors(day.Date, range).Select(anchor =>
            {
                var existing = shifts.GetValueOrDefault((anchor.Date, anchor.Period));
                var canonical = ShiftClock.Boundaries(anchor.Date, anchor.Period);
                // Owning periods for a boundary anchor may be outside this board's months.
                if (existing is not null && (existing.SchedulePeriodId != periods.GetValueOrDefault((anchor.Date.Year, anchor.Date.Month))?.Id || existing.StartsAt != canonical.StartsAt || existing.EndsAt != canonical.EndsAt))
                    throw new ApiException("NON_CANONICAL_SHIFT", 409);
                return new AvailabilityAnchorResponse(anchor.Date, anchor.Period.ToString(), existing?.Id);
            }).ToArray();
            var canonicalId = range is ShiftTimeRange.DAY or ShiftTimeRange.NIGHT ? anchors.Single().ShiftId : null;
            var saved = savedCells.GetValueOrDefault((definition.Id, day.Date));
            var members = saved?.Assignments.OrderBy(a => a.UserId).Select(a => new ScheduledEmployee(a.UserId,
                $"{a.User.FirstName} {a.User.LastName}", !a.GeneratedByRunId.HasValue || a.IsLocked || a.IsShiftLead || a.Tasks.Count > 0 || !definition.IncludeInAutomaticScheduling || definition.RequiredEmployees == 0, a.IsShiftLead)).ToArray();
            return new BoardCellResponse(day.Date, canonicalId, saved?.StartsAt ?? boundaries.StartsAt, saved?.EndsAt ?? boundaries.EndsAt,
                saved is not null ? "SAVED_DRAFT" : definition.RequiredEmployees == 0 ? "NOT_REQUIRED" : "UNASSIGNED", anchors,
                members, saved?.RequiredEmployees, saved is null ? null : Math.Max(0, saved.RequiredEmployees - saved.Assignments.Count));
        }).ToArray())).ToArray();
        await tx.CommitAsync();
        return new(start, end, ZoneId, "UNPUBLISHED", days, rows);
    }

    public async Task<AutomaticSchedulingConfigurationResponse> GetConfiguration()
    {
        var rows = await List();
        return new(ZoneId, "UNPUBLISHED", rows.Where(x => x.IncludeInAutomaticScheduling && x.RequiredEmployees > 0).ToArray(),
            rows.Where(x => !x.IncludeInAutomaticScheduling || x.RequiredEmployees == 0).Select(x => x.Id).ToArray());
    }
}
