using ITTicketIntelligence.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ITTicketIntelligence.Infrastructure.Persistence.Configurations;

internal sealed class TeamMemberConfiguration : IEntityTypeConfiguration<TeamMember>
{
    public void Configure(EntityTypeBuilder<TeamMember> builder)
    {
        builder.ToTable("team_members", table => table.HasCheckConstraint("ck_team_members_timestamps", "updated_at = created_at"));
        builder.HasKey(member => new { member.TeamId, member.UserId });
        builder.Property(member => member.TeamId).ValueGeneratedNever();
        builder.Property(member => member.UserId).ValueGeneratedNever();
        builder.Property(member => member.CreatedAt).IsRequired();
        builder.Property<DateTimeOffset>("UpdatedAt").IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(member => member.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(member => member.UserId).HasDatabaseName("ix_team_members_user");
    }
}
