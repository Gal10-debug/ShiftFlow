using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShiftManager.Domain;

namespace ShiftManager.Data.Configurations;

public sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> b)
    {
        b.Property(x => x.FirstName).IsRequired().HasMaxLength(100);
        b.Property(x => x.LastName).IsRequired().HasMaxLength(100);
        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_User_FirstName", "btrim(\"FirstName\") <> ''");
            t.HasCheckConstraint("CK_User_LastName", "btrim(\"LastName\") <> ''");
        });
    }
}
