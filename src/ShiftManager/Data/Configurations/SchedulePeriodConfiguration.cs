using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShiftManager.Domain;

namespace ShiftManager.Data.Configurations;

public sealed class SchedulePeriodConfiguration : IEntityTypeConfiguration<SchedulePeriod>
{
    public void Configure(EntityTypeBuilder<SchedulePeriod> b)
    {
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.Year, x.Month }).IsUnique();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.HasOne(x => x.AvailabilityWindow).WithOne(x => x.SchedulePeriod).HasForeignKey<AvailabilityWindow>(x => x.SchedulePeriodId);
        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Period_Month", "\"Month\" BETWEEN 1 AND 12");
            t.HasCheckConstraint("CK_Period_Year", "\"Year\" BETWEEN 1 AND 9999");
            t.HasCheckConstraint("CK_Period_Status", "\"Status\" IN ('DRAFT','PUBLISHED')");
        });
    }
}
