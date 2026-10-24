using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Domain;

namespace ShiftManager.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
 : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        var roles = new[] { "ADMIN", "COORDINATOR", "EMPLOYEE" };
        // IdentityRole initializes ConcurrencyStamp randomly; seed stamps must remain stable.
        var stamps = new[] { "f195d2c6-5bee-4348-89db-9a81bc73c9f1", "2ca2b340-2360-4d92-8593-f7e4d75f41fa", "5c1049e7-1179-4902-8086-f1eef67fc700" };
        for (var i = 0; i < roles.Length; i++)
            builder.Entity<IdentityRole<Guid>>().HasData(new IdentityRole<Guid>
            {
                Id = Guid.Parse($"00000000-0000-0000-0000-{i + 1:000000000000}"),
                Name = roles[i],
                NormalizedName = roles[i],
                ConcurrencyStamp = stamps[i]
            });
        foreach (var fk in builder.Model.GetEntityTypes().SelectMany(x => x.GetForeignKeys()))
            fk.DeleteBehavior = DeleteBehavior.Restrict;
    }
}
