using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShiftManager.Domain;

namespace ShiftManager.Data.Configurations;

public sealed class AvailabilitySubmissionConfiguration : IEntityTypeConfiguration<AvailabilitySubmission>
{
    public void Configure(EntityTypeBuilder<AvailabilitySubmission> b)
    {
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.UserId, x.SchedulePeriodId }).IsUnique();
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
        b.HasOne(x => x.SchedulePeriod).WithMany().HasForeignKey(x => x.SchedulePeriodId);
        b.ToTable(t => t.HasCheckConstraint("CK_Submission_Version", "\"Version\" > 0"));
    }
}
