using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShiftManager.Domain;
namespace ShiftManager.Data.Configurations;

public sealed class PublicationControlConfiguration : IEntityTypeConfiguration<PublicationControl>
{
    public void Configure(EntityTypeBuilder<PublicationControl> b)
    {
        b.ToTable("PublicationControl"); b.HasKey(x => x.PublicationId);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasOne<SchedulePublication>().WithOne().HasForeignKey<PublicationControl>(x => x.PublicationId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class ShiftExchangeConfiguration : IEntityTypeConfiguration<ShiftExchange>
{
    public void Configure(EntityTypeBuilder<ShiftExchange> b)
    {
        b.ToTable("ShiftExchange", t => t.HasCheckConstraint("CK_Exchange_Status", "\"Status\" IN ('AWAITING_REPLACEMENT','AWAITING_ADMIN','APPROVED_PENDING_PUBLICATION','APPLIED','REJECTED','CANCELLED','SUPERSEDED')"));
        b.HasKey(x => x.Id); b.Property(x => x.Version).IsConcurrencyToken();
        b.Property(x => x.Status).HasMaxLength(32); b.Property(x => x.Reason).HasMaxLength(2000);
        b.HasIndex(x => new { x.SchedulePeriodId, x.Status });
        b.HasOne<SchedulePeriod>().WithMany().HasForeignKey(x => x.SchedulePeriodId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.RequesterUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ReplacementUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Assignments).WithOne().HasForeignKey(x => x.ShiftExchangeId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class ShiftExchangeAssignmentConfiguration : IEntityTypeConfiguration<ShiftExchangeAssignment>
{
    public void Configure(EntityTypeBuilder<ShiftExchangeAssignment> b)
    {
        b.ToTable("ShiftExchangeAssignment"); b.HasKey(x => x.Id);
        b.Property(x => x.Note).HasMaxLength(1000);
        b.HasIndex(x => new { x.ShiftExchangeId, x.SourceAssignmentId }).IsUnique();
        b.HasOne<SchedulePublication>().WithMany().HasForeignKey(x => x.PublicationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<SchedulePublication>().WithMany().HasForeignKey(x => x.AppliedPublicationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ShiftTypeDefinition>().WithMany().HasForeignKey(x => x.DefinitionId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class ShiftExchangeReservationConfiguration : IEntityTypeConfiguration<ShiftExchangeReservation>
{
    public void Configure(EntityTypeBuilder<ShiftExchangeReservation> b)
    {
        b.ToTable("ShiftExchangeReservation"); b.HasKey(x => x.AssignmentId);
        b.HasOne<ShiftExchange>().WithMany().HasForeignKey(x => x.ShiftExchangeId).OnDelete(DeleteBehavior.Restrict);
    }
}
