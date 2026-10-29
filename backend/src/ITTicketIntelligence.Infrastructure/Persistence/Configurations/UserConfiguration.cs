using ITTicketIntelligence.Domain.Enums;
using ITTicketIntelligence.Domain.Identity;
using ITTicketIntelligence.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ITTicketIntelligence.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        ConfigurationRules.Entity(builder, "users", mutable: true);
        builder.Property(user => user.Name).HasMaxLength(100).IsRequired();
        builder.Property(user => user.Email).HasConversion(email => email.Value, value => EmailAddress.Create(value)).HasMaxLength(254).IsRequired();
        builder.Property(user => user.Role).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(user => user.IsActive).IsRequired();
        builder.Property<string?>("PasswordHash").HasMaxLength(200);
        builder.Property<Guid>("AuthVersion").HasDefaultValue(Guid.Empty);
        builder.Property<int>("LoginFailures").HasDefaultValue(0);
        builder.Property<DateTimeOffset?>("LockoutUntil");
        builder.HasIndex(user => user.Email).IsUnique().HasDatabaseName("ux_users_email");
        builder.ToTable("users", table =>
        {
            table.HasCheckConstraint("ck_users_text", ConfigurationRules.NotBlank("name", "email"));
            table.HasCheckConstraint("ck_users_email_normalized", "email = lower(btrim(email))");
            table.HasCheckConstraint("ck_users_role", ConfigurationRules.EnumValues<UserRole>("role"));
        });
    }
}
