using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShiftManager.Domain;

namespace ShiftManager.Data.Configurations;

public sealed class ShiftPreferenceConfiguration : IEntityTypeConfiguration<ShiftPreference>
{
    public void Configure(EntityTypeBuilder<ShiftPreference> b)
    {
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.UserId, x.ShiftId }).IsUnique();
        b.Property(x => x.PreferenceType).HasConversion<string>().HasMaxLength(16);
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
        b.HasOne(x => x.Shift).WithMany(x => x.Preferences).HasForeignKey(x => x.ShiftId);
        b.ToTable(t => t.HasCheckConstraint("CK_Preference_Type", "\"PreferenceType\" IN ('BLOCKED','PREFERRED')"));
    }
}
