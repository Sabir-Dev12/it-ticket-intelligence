using ITTicketIntelligence.Domain.Identity;
using ITTicketIntelligence.Domain.Tickets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ITTicketIntelligence.Infrastructure.Persistence.Configurations;

internal sealed class TicketCommentConfiguration : IEntityTypeConfiguration<TicketComment>
{
    public void Configure(EntityTypeBuilder<TicketComment> builder)
    {
        ConfigurationRules.Entity(builder, "ticket_comments");
        builder.Property(comment => comment.TicketId).IsRequired();
        builder.Property(comment => comment.AuthorId).IsRequired();
        builder.Property(comment => comment.Message).HasMaxLength(4000).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(comment => comment.AuthorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(comment => new { comment.TicketId, comment.CreatedAt }).HasDatabaseName("ix_ticket_comments_ticket_created");
        builder.ToTable("ticket_comments", table =>
        {
            table.HasCheckConstraint("ck_ticket_comments_message", ConfigurationRules.NotBlank("message"));
            table.HasCheckConstraint("ck_ticket_comments_immutable_time", "updated_at = created_at");
        });
    }
}
