using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShiftManager.Domain;
namespace ShiftManager.Data.Configurations;

public sealed class OrganizationalTeamConfiguration : IEntityTypeConfiguration<OrganizationalTeam>
{
    public void Configure(EntityTypeBuilder<OrganizationalTeam> b)
    {
        b.ToTable("OrganizationalTeam", t => {
            t.HasCheckConstraint("CK_Team_Slot", "\"Slot\" BETWEEN 1 AND 4");
            t.HasCheckConstraint("CK_Team_DistinctLeadership", "\"LeaderUserId\" IS NULL OR \"DeputyUserId\" IS NULL OR \"LeaderUserId\" <> \"DeputyUserId\"");
        });
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.Slot).IsUnique();
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasOne<TeamMembership>().WithMany().HasForeignKey(x => new { x.Id, x.LeaderUserId }).HasPrincipalKey(x => new { x.TeamId, x.UserId });
        b.HasOne<TeamMembership>().WithMany().HasForeignKey(x => new { x.Id, x.DeputyUserId }).HasPrincipalKey(x => new { x.TeamId, x.UserId });
        var names = new[] { "צוות א׳", "צוות ב׳", "צוות ג׳", "צוות ד׳" };
        for (var i = 1; i <= 4; i++) b.HasData(new OrganizationalTeam { Id = Guid.Parse($"40000000-0000-0000-0000-{i:000000000000}"), Slot = i, Name = names[i - 1] });
    }
}
public sealed class TeamMembershipConfiguration : IEntityTypeConfiguration<TeamMembership>
{
    public void Configure(EntityTypeBuilder<TeamMembership> b)
    {
        b.ToTable("TeamMembership"); b.HasKey(x => x.UserId);
        b.HasAlternateKey(x => new { x.TeamId, x.UserId });
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId);
        b.HasOne<OrganizationalTeam>().WithMany().HasForeignKey(x => x.TeamId);
    }
}
public sealed class DelegatedAvailabilityAuditConfiguration : IEntityTypeConfiguration<DelegatedAvailabilityAudit>
{
    public void Configure(EntityTypeBuilder<DelegatedAvailabilityAudit> b)
    {
        b.ToTable("DelegatedAvailabilityAudit"); b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.TeamId, x.OccurredAt });
        b.Property(x => x.PreviousValues).HasColumnType("jsonb"); b.Property(x => x.NewValues).HasColumnType("jsonb");
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.TargetUserId);
        b.HasOne<OrganizationalTeam>().WithMany().HasForeignKey(x => x.TeamId);
        b.HasOne<SchedulePeriod>().WithMany().HasForeignKey(x => x.SchedulePeriodId);
    }
}
