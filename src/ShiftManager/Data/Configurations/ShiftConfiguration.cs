using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShiftManager.Domain;

namespace ShiftManager.Data.Configurations;

public sealed class ShiftConfiguration : IEntityTypeConfiguration<Shift>
{
    public void Configure(EntityTypeBuilder<Shift> b)
    {
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.ShiftDate, x.ShiftType }).IsUnique();
        b.Property(x => x.ShiftType).HasConversion<string>().HasMaxLength(16);
        b.HasOne(x => x.SchedulePeriod).WithMany(x => x.Shifts).HasForeignKey(x => x.SchedulePeriodId);
        b.HasOne(x => x.CoordinatorUser).WithMany().HasForeignKey(x => x.CoordinatorUserId);
        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Shift_Time", "\"EndsAt\" > \"StartsAt\"");
            t.HasCheckConstraint("CK_Shift_Type", "\"ShiftType\" IN ('DAY','NIGHT')");
        });
    }
}
