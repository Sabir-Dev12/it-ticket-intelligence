using ITTicketIntelligence.Domain.Auditing;
using ITTicketIntelligence.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ITTicketIntelligence.Infrastructure.Persistence.Configurations;

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        ConfigurationRules.Entity(builder, "audit_logs");
        builder.Property(log => log.UserId);
        builder.Property(log => log.Action).HasMaxLength(128).IsRequired();
        builder.Property(log => log.EntityType).HasMaxLength(128).IsRequired();
        builder.Property(log => log.EntityId).IsRequired();
        builder.Property(log => log.OccurredAt).IsRequired();
        builder.Property(log => log.Details).HasMaxLength(4000);
        builder.Property(log => log.CorrelationId);
        builder.HasOne<User>().WithMany().HasForeignKey(log => log.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(log => new { log.EntityType, log.EntityId, log.OccurredAt }).IsDescending(false, false, true).HasDatabaseName("ix_audit_target_occurred");
        builder.HasIndex(log => new { log.UserId, log.OccurredAt }).IsDescending(false, true).HasDatabaseName("ix_audit_actor_occurred");
        builder.HasIndex(log => log.CorrelationId).HasFilter("correlation_id IS NOT NULL").HasDatabaseName("ix_audit_correlation");
        builder.ToTable("audit_logs", table =>
        {
            table.HasCheckConstraint("ck_audit_text", ConfigurationRules.NotBlank("action", "entity_type"));
            table.HasCheckConstraint("ck_audit_target", "entity_id <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("ck_audit_immutable_time", "updated_at = created_at");
            table.HasCheckConstraint("ck_audit_correlation", "correlation_id IS NULL OR correlation_id <> '00000000-0000-0000-0000-000000000000'::uuid");
        });
    }
}
