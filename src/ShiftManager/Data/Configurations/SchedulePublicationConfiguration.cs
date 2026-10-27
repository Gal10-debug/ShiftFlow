using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShiftManager.Domain;

namespace ShiftManager.Data.Configurations;

public sealed class SchedulePublicationConfiguration : IEntityTypeConfiguration<SchedulePublication>
{
    public void Configure(EntityTypeBuilder<SchedulePublication> b)
    {
        b.Property(x => x.Snapshot).HasColumnType("jsonb");
        b.Property(x => x.PublicationType).HasMaxLength(16);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.Property(x => x.InputFingerprint).HasMaxLength(64);
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.SchedulePeriodId, x.VersionNumber }).IsUnique();
        b.HasOne(x => x.SchedulePeriod).WithMany(x => x.Publications).HasForeignKey(x => x.SchedulePeriodId);
        b.HasOne(x => x.PublishedByUser).WithMany().HasForeignKey(x => x.PublishedByUserId);
        b.ToTable(t => t.HasCheckConstraint("CK_Publication_Version", "\"VersionNumber\" > 0"));
    }
}
