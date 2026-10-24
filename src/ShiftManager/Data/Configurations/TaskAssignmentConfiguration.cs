using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShiftManager.Domain;

namespace ShiftManager.Data.Configurations;

public sealed class TaskAssignmentConfiguration : IEntityTypeConfiguration<TaskAssignment>
{
    public void Configure(EntityTypeBuilder<TaskAssignment> b)
    {
        b.HasKey(x => x.Id);
        b.HasOne(x => x.ShiftTask).WithMany(x => x.Assignments).HasForeignKey(x => x.ShiftTaskId);
        b.HasOne(x => x.ShiftAssignment).WithMany(x => x.Tasks).HasForeignKey(x => x.ShiftAssignmentId);
        b.ToTable(t => t.HasCheckConstraint("CK_TaskAssignment_Time", "\"EndsAt\" > \"StartsAt\""));
    }
}
