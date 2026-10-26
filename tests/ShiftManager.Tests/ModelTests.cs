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
