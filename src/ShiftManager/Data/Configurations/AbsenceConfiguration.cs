using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShiftManager.Domain;
namespace ShiftManager.Data.Configurations;

public sealed class AbsenceRequestConfiguration : IEntityTypeConfiguration<AbsenceRequest>
{
    public void Configure(EntityTypeBuilder<AbsenceRequest> b)
    {
        b.HasKey(x => x.Id);
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
        b.HasOne(x => x.Replaces).WithMany().HasForeignKey(x => x.ReplacesId);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Property(x => x.Kind).HasMaxLength(16);
        b.Property(x => x.Status).HasMaxLength(16);
        b.Property(x => x.Reason).HasMaxLength(16);
        b.Property(x => x.Explanation).HasMaxLength(2000);
        b.HasIndex(x => new { x.UserId, x.Status, x.StartDate });
        b.HasIndex(x => new { x.Status, x.CreatedAt });
        b.HasIndex(x => x.ReplacesId).IsUnique().HasFilter("\"Status\" = 'PENDING' AND \"ReplacesId\" IS NOT NULL");
        b.ToTable(t => {
            t.HasCheckConstraint("CK_Absence_Status", "\"Status\" IN ('PENDING','APPROVED','REJECTED','CANCELLED')");
            t.HasCheckConstraint("CK_Absence_Reason", "\"Reason\" IN ('STUDIES','RESERVE','VACATION','OTHER')");
            t.HasCheckConstraint("CK_Absence_Kind", "(\"Kind\" = 'CREATE' AND \"ReplacesId\" IS NULL AND \"ReplacesVersion\" IS NULL) OR (\"Kind\" IN ('MODIFY','CANCEL') AND \"ReplacesId\" IS NOT NULL AND \"ReplacesVersion\" > 0)");
            t.HasCheckConstraint("CK_Absence_Dates", "\"StartDate\" BETWEEN '0002-01-01' AND '9998-12-30' AND (\"EndDate\" IS NULL OR \"EndDate\" BETWEEN \"StartDate\" AND '9998-12-30') AND (\"Recurring\" OR \"EndDate\" IS NOT NULL)");
            t.HasCheckConstraint("CK_Absence_Times", "(\"FullDay\" AND \"StartTime\" IS NULL AND \"EndTime\" IS NULL) OR (NOT \"FullDay\" AND \"StartTime\" IS NOT NULL AND \"EndTime\" IS NOT NULL AND \"StartTime\" <> \"EndTime\")");
            t.HasCheckConstraint("CK_Absence_Version", "\"Version\" > 0");
        });
    }
}
public sealed class AbsenceWeekdayConfiguration : IEntityTypeConfiguration<AbsenceWeekday>
{
    public void Configure(EntityTypeBuilder<AbsenceWeekday> b)
    {
        b.HasKey(x => new { x.AbsenceRequestId, x.Day });
        b.HasOne(x => x.AbsenceRequest).WithMany(x => x.Weekdays).HasForeignKey(x => x.AbsenceRequestId);
        b.ToTable(t => t.HasCheckConstraint("CK_AbsenceWeekday_Day", "\"Day\" BETWEEN 0 AND 6"));
    }
}
public sealed class AbsenceDecisionConfiguration : IEntityTypeConfiguration<AbsenceDecision>
{
    public void Configure(EntityTypeBuilder<AbsenceDecision> b)
    {
        b.HasKey(x => x.Id);
        b.HasOne(x => x.AbsenceRequest).WithMany().HasForeignKey(x => x.AbsenceRequestId);
        b.HasOne(x => x.ActorUser).WithMany().HasForeignKey(x => x.ActorUserId);
        b.HasIndex(x => x.AbsenceRequestId).IsUnique();
        b.Property(x => x.Decision).HasMaxLength(16);
        b.Property(x => x.RejectionReason).HasMaxLength(2000);
        b.ToTable(t => t.HasCheckConstraint("CK_AbsenceDecision_Value", "\"Decision\" IN ('APPROVED','REJECTED','CANCELLED') AND (\"Decision\" <> 'REJECTED' OR length(btrim(\"RejectionReason\")) > 0 AND \"RejectionReason\" IS NOT NULL)"));
    }
}
public sealed class AbsenceConflictConfiguration : IEntityTypeConfiguration<AbsenceConflict>
{
    public void Configure(EntityTypeBuilder<AbsenceConflict> b)
    {
        b.HasKey(x => x.Id);
        b.HasOne(x => x.AbsenceRequest).WithMany().HasForeignKey(x => x.AbsenceRequestId);
        b.HasOne(x => x.SchedulePeriod).WithMany().HasForeignKey(x => x.SchedulePeriodId);
        b.HasOne(x => x.Publication).WithMany().HasForeignKey(x => x.PublicationId);
        b.HasOne(x => x.ResolvedByPublication).WithMany().HasForeignKey(x => x.ResolvedByPublicationId);
        b.Property(x => x.ShiftName).HasMaxLength(100);
        b.HasIndex(x => new { x.AbsenceRequestId, x.PublicationId, x.AssignmentId }).IsUnique();
        b.HasIndex(x => new { x.SchedulePeriodId, x.ResolvedAt });
    }
}
