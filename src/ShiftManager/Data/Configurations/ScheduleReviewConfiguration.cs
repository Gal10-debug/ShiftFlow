using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShiftManager.Domain;
namespace ShiftManager.Data.Configurations;
public sealed class ScheduleIssueConfiguration : IEntityTypeConfiguration<ScheduleIssue>
{
    public void Configure(EntityTypeBuilder<ScheduleIssue> b)
    {
        b.ToTable("ScheduleIssue", t => t.HasCheckConstraint("CK_Issue_Status", "\"Status\" IN ('OPEN','IN_REVIEW','RESOLVED','REJECTED')"));
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.PublicationId, x.AssignmentId, x.ReporterUserId }).IsUnique();
        b.Property(x => x.Explanation).HasMaxLength(2000); b.Property(x => x.Response).HasMaxLength(2000);
        b.Property(x => x.Status).HasMaxLength(16); b.Property(x => x.Version).IsConcurrencyToken();
        b.HasOne<SchedulePeriod>().WithMany().HasForeignKey(x => x.SchedulePeriodId);
        b.HasOne<SchedulePublication>().WithMany().HasForeignKey(x => x.PublicationId);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ReporterUserId);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ReviewedByUserId);
    }
}
public sealed class ScheduleEditAuditConfiguration : IEntityTypeConfiguration<ScheduleEditAudit>
{
    public void Configure(EntityTypeBuilder<ScheduleEditAudit> b)
    {
        b.ToTable("ScheduleEditAudit"); b.HasKey(x => x.Id);
        b.Property(x => x.Before).HasColumnType("jsonb"); b.Property(x => x.After).HasColumnType("jsonb");
        b.Property(x => x.Operation).HasMaxLength(32); b.HasIndex(x => new { x.SchedulePeriodId, x.OccurredAt });
        b.HasOne<SchedulePeriod>().WithMany().HasForeignKey(x => x.SchedulePeriodId);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId);
    }
}
