using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShiftManager.Domain;

namespace ShiftManager.Data.Configurations;

public sealed class AvailabilityWindowConfiguration : IEntityTypeConfiguration<AvailabilityWindow>
{
    public void Configure(EntityTypeBuilder<AvailabilityWindow> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.HasOne(x => x.OpenedByUser).WithMany().HasForeignKey(x => x.OpenedByUserId);
        b.HasOne(x => x.ClosedByUser).WithMany().HasForeignKey(x => x.ClosedByUserId);
        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Window_Time", "\"ClosesAt\" IS NULL OR \"ClosesAt\" >= \"OpensAt\"");
            t.HasCheckConstraint("CK_Window_Status", "\"Status\" IN ('OPEN','CLOSED')");
        });
    }
}
