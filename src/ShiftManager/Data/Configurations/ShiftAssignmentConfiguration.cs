using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShiftManager.Domain;

namespace ShiftManager.Data.Configurations;

public sealed class ShiftAssignmentConfiguration : IEntityTypeConfiguration<ShiftAssignment>
{
    public void Configure(EntityTypeBuilder<ShiftAssignment> b)
    {
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.ShiftId, x.UserId }).IsUnique();
        b.HasIndex(x => x.ShiftId).IsUnique().HasFilter("\"IsShiftLead\" = TRUE");
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
        b.HasOne(x => x.Shift).WithMany(x => x.Assignments).HasForeignKey(x => x.ShiftId);
    }
}
