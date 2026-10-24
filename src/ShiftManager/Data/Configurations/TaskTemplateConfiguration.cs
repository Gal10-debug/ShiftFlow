using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShiftManager.Domain;

namespace ShiftManager.Data.Configurations;

public sealed class TaskTemplateConfiguration : IEntityTypeConfiguration<TaskTemplate>
{
    public void Configure(EntityTypeBuilder<TaskTemplate> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired().HasMaxLength(200);
        b.Property(x => x.Description).IsRequired();
        b.ToTable(t => t.HasCheckConstraint("CK_Template_Name", "btrim(\"Name\") <> ''"));
    }
}
