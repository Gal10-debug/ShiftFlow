using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using ShiftManager.Data;
using ShiftManager.Domain;
using ShiftManager.Services;

namespace ShiftManager.Tests;

public class ModelTests
{
    private static ApplicationDbContext CreateContext() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql("Host=localhost;Database=model_only").Options);

    [Fact]
    public void All_business_entities_have_guid_keys_and_explicit_restrict_foreign_keys()
    {
        using var db = CreateContext();
        Type[] types = [typeof(SchedulePeriod), typeof(AvailabilityWindow), typeof(AvailabilitySubmission), typeof(ShiftTypeDefinition), typeof(Shift), typeof(ShiftPreference),
            typeof(ShiftAssignment), typeof(TaskTemplate), typeof(ShiftTask), typeof(TaskAssignment), typeof(SchedulePublication)];
        foreach (var type in types)
        {
            var entity = db.Model.FindEntityType(type)!;
            Assert.Equal(typeof(Guid), Assert.Single(entity.FindPrimaryKey()!.Properties).ClrType);
        }
        Assert.All(db.Model.GetEntityTypes().SelectMany(x => x.GetForeignKeys()),
            fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
    }

    [Fact]
    public void Identity_and_required_fields_are_configured()
    {
        using var db = CreateContext();
        var model = db.GetService<IDesignTimeModel>().Model;
        var roles = model.FindEntityType(typeof(IdentityRole<Guid>))!.GetSeedData()
            .Select(x => x["Name"]).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "ADMIN", "COORDINATOR", "EMPLOYEE" }, roles);
        foreach (var (type, property) in new[] { (typeof(ApplicationUser), "FirstName"), (typeof(ApplicationUser), "LastName"),
                     (typeof(TaskTemplate), "Name"), (typeof(ShiftTask), "Title") })
            Assert.False(model.FindEntityType(type)!.FindProperty(property)!.IsNullable);
        Assert.Equal(typeof(Guid), model.FindEntityType(typeof(ApplicationUser))!.FindPrimaryKey()!.Properties[0].ClrType);
    }

    [Fact]
    public void Lead_index_is_filtered_and_window_is_one_to_one()
    {
        using var db = CreateContext();
        var assignment = db.Model.FindEntityType(typeof(ShiftAssignment))!;
        Assert.Contains(assignment.GetIndexes(), x => x.IsUnique && x.GetFilter() == "\"IsShiftLead\" = TRUE");
        var window = db.Model.FindEntityType(typeof(AvailabilityWindow))!;
        Assert.True(window.GetForeignKeys().Single(x => x.PrincipalEntityType.ClrType == typeof(SchedulePeriod)).IsUnique);
    }

    [Theory]
    [InlineData(2026, 3, 26, 11)]
    [InlineData(2026, 10, 24, 13)]
    [InlineData(2026, 1, 10, 12)]
    public void Night_shift_preserves_local_boundaries_across_dst(int year, int month, int day, int hours)
    {
        var date = new DateOnly(year, month, day);
        var (start, end) = ShiftClock.Boundaries(date, ShiftType.NIGHT);
        Assert.Equal(DateTimeKind.Utc, start.Kind);
        Assert.Equal(hours, (end - start).TotalHours);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Jerusalem");
        Assert.Equal(date.ToDateTime(new TimeOnly(19, 0)), TimeZoneInfo.ConvertTimeFromUtc(start, zone));
        Assert.Equal(date.AddDays(1).ToDateTime(new TimeOnly(7, 0)), TimeZoneInfo.ConvertTimeFromUtc(end, zone));
    }

    [Theory]
    [InlineData("2026-10-01", "2026-09-27")]
    [InlineData("2026-10-25", "2026-10-25")]
    [InlineData("2027-01-01", "2026-12-27")]
    [InlineData("2027-01-02", "2026-12-27")]
    public void Availability_weeks_start_on_sunday_across_month_and_year_boundaries(string date, string sunday)
        => Assert.Equal(DateOnly.Parse(sunday), AvailabilityService.WeekStart(DateOnly.Parse(date)));

    [Fact]
    public void Day_shift_uses_local_seven_to_nineteen()
    {
        var (start, end) = ShiftClock.Boundaries(new DateOnly(2026, 10, 24), ShiftType.DAY);
        Assert.Equal(12, (end - start).TotalHours);
        Assert.Equal(4, start.Hour);
    }
}

public sealed class ShiftTimeRangeTests
{
    [Theory]
    [InlineData(ShiftTimeRange.H06_09, 6, 9, 0)]
    [InlineData(ShiftTimeRange.H07_13, 7, 13, 0)]
    [InlineData(ShiftTimeRange.H07_15, 7, 15, 0)]
    [InlineData(ShiftTimeRange.H15_23, 15, 23, 0)]
    [InlineData(ShiftTimeRange.H23_07, 23, 7, 1)]
    public void Custom_ranges_preserve_local_clock_times_and_next_day_ownership(ShiftTimeRange range, int startHour, int endHour, int nextDay)
    {
        var date = new DateOnly(2026, 10, 31);
        var (start, end) = ShiftTimeRanges.Boundaries(date, range);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Jerusalem");
        Assert.Equal(date.ToDateTime(new TimeOnly(startHour, 0)), TimeZoneInfo.ConvertTimeFromUtc(start, zone));
        Assert.Equal(date.AddDays(nextDay).ToDateTime(new TimeOnly(endHour, 0)), TimeZoneInfo.ConvertTimeFromUtc(end, zone));
    }
    [Theory]
    [InlineData(2026, 3, 26, 7)]
    [InlineData(2026, 10, 24, 9)]
    public void Late_night_range_handles_DST_using_separate_boundaries(int year, int month, int day, int hours)
    {
        var (start, end) = ShiftTimeRanges.Boundaries(new(year, month, day), ShiftTimeRange.H23_07);
        Assert.Equal(hours, (end - start).TotalHours);
    }
    [Fact]
    public void Overlap_contract_includes_previous_night_and_cross_period_blocks_without_touching_endpoints()
    {
        var date = new DateOnly(2026, 11, 1);
        Assert.Equal(new[] { (date.AddDays(-1), ShiftType.NIGHT), (date, ShiftType.DAY) }, ShiftTimeRanges.AvailabilityAnchors(date, ShiftTimeRange.H06_09));
        Assert.Equal(new[] { (date, ShiftType.DAY), (date, ShiftType.NIGHT) }, ShiftTimeRanges.AvailabilityAnchors(date, ShiftTimeRange.H15_23));
        Assert.Equal(new[] { (date, ShiftType.NIGHT) }, ShiftTimeRanges.AvailabilityAnchors(date, ShiftTimeRange.H23_07));
        Assert.Equal(new[] { (date, ShiftType.DAY) }, ShiftTimeRanges.AvailabilityAnchors(date, ShiftTimeRange.H07_13));
    }
}
