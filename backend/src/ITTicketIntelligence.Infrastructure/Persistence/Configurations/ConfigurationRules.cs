using ITTicketIntelligence.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ITTicketIntelligence.Infrastructure.Persistence.Configurations;

internal static class ConfigurationRules
{
    public static void Entity<TEntity>(EntityTypeBuilder<TEntity> builder, string table, bool mutable = false) where TEntity : Entity
    {
        builder.HasBaseType((Type?)null);
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).ValueGeneratedNever();
        builder.Property<DateTimeOffset>("CreatedAt").IsRequired();
        builder.Property<DateTimeOffset>("UpdatedAt").IsRequired();
        if (mutable) builder.Property<uint>("xmin").IsRowVersion();
        builder.ToTable(table, definition =>
        {
            definition.HasCheckConstraint($"ck_{table}_id", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
            definition.HasCheckConstraint($"ck_{table}_timestamps", "updated_at >= created_at");
        });
    }

    public static string EnumValues<T>(string column) where T : struct, Enum =>
        $"{column} IN ({string.Join(", ", Enum.GetNames<T>().Select(name => $"'{name}'"))})";

    public static string NotBlank(params string[] columns) => string.Join(" AND ", columns.Select(column => $"{column} ~ '[^[:space:]]'"));
}
