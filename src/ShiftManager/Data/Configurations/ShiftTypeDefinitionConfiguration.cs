using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShiftManager.Domain;

namespace ShiftManager.Data.Configurations;

public sealed class ShiftTypeDefinitionConfiguration : IEntityTypeConfiguration<ShiftTypeDefinition>
{
    public void Configure(EntityTypeBuilder<ShiftTypeDefinition> b)
    {
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.Slot).IsUnique();
        // The migration makes this uniqueness constraint deferred to allow atomic swaps.
        b.HasIndex(x => x.DisplayOrder).IsUnique();
        b.Property(x => x.Name).IsRequired().HasMaxLength(100);
        b.Property(x => x.Period).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.TimeRange).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_ShiftTypeDefinition_Slot", "\"Slot\" BETWEEN 1 AND 51");
            t.HasCheckConstraint("CK_ShiftTypeDefinition_Order", "\"DisplayOrder\" BETWEEN 1 AND 51");
            t.HasCheckConstraint("CK_ShiftTypeDefinition_Name", "length(btrim(\"Name\")) > 0");
            t.HasCheckConstraint("CK_ShiftTypeDefinition_Period", "\"Period\" IN ('DAY','NIGHT')");
            t.HasCheckConstraint("CK_ShiftTypeDefinition_TimeRange", "\"TimeRange\" IS NULL OR (\"TimeRange\" IN ('DAY','NIGHT','H06_09','H07_13','H07_15','H15_23','H23_07') AND \"Period\" = CASE WHEN \"TimeRange\" IN ('NIGHT','H23_07') THEN 'NIGHT' ELSE 'DAY' END)");
            t.HasCheckConstraint("CK_ShiftTypeDefinition_Staffing", "\"RequiredEmployees\" BETWEEN 0 AND 1000");
            t.HasCheckConstraint("CK_ShiftTypeDefinition_Version", "\"Version\" > 0");
        });
        // Stable migration seeds only. Never re-seed or update administrator edits at startup.
        for (var i = 1; i <= 51; i++)
            b.HasData(new ShiftTypeDefinition
            {
                Id = Guid.Parse($"40000000-0000-0000-0000-{i:000000000000}"), Slot = i,
                Name = $"משמרת {i}", DisplayOrder = i, Period = ShiftType.DAY,
                RequiredEmployees = 0, IncludeInAutomaticScheduling = false, Version = 1,
                UpdatedAt = new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc)
            });
    }
}
