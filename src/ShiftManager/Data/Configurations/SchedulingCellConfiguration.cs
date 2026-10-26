using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShiftManager.Domain;
namespace ShiftManager.Data.Configurations;

public sealed class SchedulingCellConfiguration : IEntityTypeConfiguration<SchedulingCell>
{
    public void Configure(EntityTypeBuilder<SchedulingCell> b)
    {
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.ShiftTypeDefinitionId, x.Date }).IsUnique();
        b.HasOne(x => x.Definition).WithMany().HasForeignKey(x => x.ShiftTypeDefinitionId);
        b.HasOne(x => x.SchedulePeriod).WithMany().HasForeignKey(x => x.SchedulePeriodId);
        b.HasOne(x => x.Run).WithMany().HasForeignKey(x => x.SchedulingRunId);
        b.ToTable(t => {
            t.HasCheckConstraint("CK_Cell_Required", "\"RequiredEmployees\" BETWEEN 0 AND 1000");
            t.HasCheckConstraint("CK_Cell_Time", "\"EndsAt\" > \"StartsAt\"");
        });
    }
}
public sealed class SchedulingRunConfiguration : IEntityTypeConfiguration<SchedulingRun>
{
    public void Configure(EntityTypeBuilder<SchedulingRun> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.InputFingerprint).HasMaxLength(64);
        b.Property(x => x.SolverStatus).HasMaxLength(32);
        b.HasOne(x => x.SavedByUser).WithMany().HasForeignKey(x => x.SavedByUserId);
    }
}
