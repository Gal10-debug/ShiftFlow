using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShiftManager.Domain;

namespace ShiftManager.Data.Configurations;

public sealed class ShiftTaskConfiguration : IEntityTypeConfiguration<ShiftTask>
{
    public void Configure(EntityTypeBuilder<ShiftTask> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Title).IsRequired().HasMaxLength(200);
        b.Property(x => x.Description).IsRequired();
        b.HasOne(x => x.Shift).WithMany(x => x.Tasks).HasForeignKey(x => x.ShiftId);
        b.HasOne(x => x.TaskTemplate).WithMany(x => x.Tasks).HasForeignKey(x => x.TaskTemplateId);
        b.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId);
        b.ToTable(t => t.HasCheckConstraint("CK_Task_Title", "btrim(\"Title\") <> ''"));
    }
}
