using ITTicketIntelligence.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ITTicketIntelligence.Infrastructure.Persistence.Configurations;

internal sealed class TeamConfiguration : IEntityTypeConfiguration<Team>
{
    public void Configure(EntityTypeBuilder<Team> builder)
    {
        ConfigurationRules.Entity(builder, "teams", mutable: true);
        builder.Property(team => team.Name).HasMaxLength(100).IsRequired();
        builder.Property(team => team.Description).HasMaxLength(1000);
        builder.Property(team => team.IsActive).IsRequired();
        builder.Property<string>("NormalizedName").HasComputedColumnSql("lower(btrim(name))", stored: true);
        builder.HasIndex("NormalizedName").IsUnique().HasDatabaseName("ux_teams_normalized_name");
        builder.Ignore(team => team.MemberIds);
        builder.HasMany(team => team.Members).WithOne().HasForeignKey(member => member.TeamId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(team => team.Members).HasField("members").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.ToTable("teams", table => table.HasCheckConstraint("ck_teams_name", ConfigurationRules.NotBlank("name")));
    }
}
