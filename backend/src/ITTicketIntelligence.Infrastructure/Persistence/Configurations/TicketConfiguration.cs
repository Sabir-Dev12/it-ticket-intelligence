using ITTicketIntelligence.Domain.Enums;
using ITTicketIntelligence.Domain.Identity;
using ITTicketIntelligence.Domain.Tickets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ITTicketIntelligence.Infrastructure.Persistence.Configurations;

internal sealed class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        ConfigurationRules.Entity(builder, "tickets", mutable: true);
        builder.Property(ticket => ticket.Reference).HasMaxLength(32).IsRequired();
        builder.Property(ticket => ticket.Title).HasMaxLength(160).IsRequired();
        builder.Property(ticket => ticket.Description).HasMaxLength(8000).IsRequired();
        builder.Property(ticket => ticket.CreatedById).IsRequired();
        builder.Property(ticket => ticket.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(ticket => ticket.Category).HasConversion<string>().HasMaxLength(32);
        builder.Property(ticket => ticket.Priority).HasConversion<string>().HasMaxLength(16);
        builder.Property(ticket => ticket.AssignedTeamId);
        builder.Property(ticket => ticket.AssignedUserId);
        builder.HasOne<User>().WithMany().HasForeignKey(ticket => ticket.AssignedUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(ticket => new { ticket.AssignedUserId, ticket.Status }).HasFilter("assigned_user_id IS NOT NULL").HasDatabaseName("ix_tickets_assignee_status");
        builder.Property(ticket => ticket.Resolution).HasMaxLength(12000);
        builder.Property(ticket => ticket.ResolvedAt);
        builder.Property(ticket => ticket.ClosedAt);
        builder.Property(ticket => ticket.IsArchived).HasDefaultValue(false).IsRequired();
        builder.Property(ticket => ticket.ArchivedAt);
        builder.Property(ticket => ticket.Revision).IsConcurrencyToken();
        builder.HasOne<User>().WithMany().HasForeignKey(ticket => ticket.CreatedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Team>().WithMany().HasForeignKey(ticket => ticket.AssignedTeamId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(ticket => ticket.Comments).WithOne().HasForeignKey(comment => comment.TicketId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(ticket => ticket.Predictions).WithOne().HasForeignKey(prediction => prediction.TicketId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(ticket => ticket.Comments).HasField("comments").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(ticket => ticket.Predictions).HasField("predictions").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(ticket => ticket.Reference).IsUnique().HasDatabaseName("ux_tickets_reference");
        builder.HasIndex(ticket => new { ticket.IsArchived, ticket.CreatedAt, ticket.Id }).IsDescending(false, true, true).HasDatabaseName("ix_tickets_archive_created_id");
        builder.HasIndex(ticket => new { ticket.CreatedById, ticket.CreatedAt }).IsDescending(false, true).HasDatabaseName("ix_tickets_creator_created");
        builder.HasIndex(ticket => new { ticket.Status, ticket.Priority, ticket.CreatedAt }).IsDescending(false, false, true).HasDatabaseName("ix_tickets_status_priority_created");
        builder.HasIndex(ticket => new { ticket.AssignedTeamId, ticket.Status }).HasFilter("assigned_team_id IS NOT NULL").HasDatabaseName("ix_tickets_team_status");
        builder.ToTable("tickets", table =>
        {
            table.HasCheckConstraint("ck_tickets_text", ConfigurationRules.NotBlank("reference", "title", "description"));
            table.HasCheckConstraint("ck_tickets_reference_normalized", "reference = upper(btrim(reference))");
            table.HasCheckConstraint("ck_tickets_status", ConfigurationRules.EnumValues<TicketStatus>("status"));
            table.HasCheckConstraint("ck_tickets_category", ConfigurationRules.EnumValues<TicketCategory>("category"));
            table.HasCheckConstraint("ck_tickets_priority", ConfigurationRules.EnumValues<TicketPriority>("priority"));
            table.HasCheckConstraint("ck_tickets_classification", "(category IS NULL) = (priority IS NULL)");
            table.HasCheckConstraint("ck_tickets_revision", "revision >= 1");
            table.HasCheckConstraint("ck_tickets_archive", "(NOT is_archived AND archived_at IS NULL) OR (is_archived AND archived_at IS NOT NULL AND archived_at >= created_at AND archived_at <= updated_at)");
            table.HasCheckConstraint("ck_tickets_lifecycle", "(status IN ('Open', 'InProgress') AND resolution IS NULL AND resolved_at IS NULL AND closed_at IS NULL) OR (status = 'Resolved' AND resolution IS NOT NULL AND resolution ~ '[^[:space:]]' AND resolved_at IS NOT NULL AND closed_at IS NULL) OR (status = 'Closed' AND resolution IS NOT NULL AND resolution ~ '[^[:space:]]' AND resolved_at IS NOT NULL AND closed_at IS NOT NULL)");
            table.HasCheckConstraint("ck_tickets_resolution_dates", "(resolved_at IS NULL OR (resolved_at >= created_at AND resolved_at <= updated_at)) AND (closed_at IS NULL OR (closed_at >= resolved_at AND closed_at <= updated_at))");
        });
    }
}
