using ITTicketIntelligence.Domain.Conversations;
using ITTicketIntelligence.Domain.Identity;
using ITTicketIntelligence.Domain.Tickets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ITTicketIntelligence.Infrastructure.Persistence.Configurations;

internal sealed class AIConversationConfiguration : IEntityTypeConfiguration<AIConversation>
{
    public void Configure(EntityTypeBuilder<AIConversation> builder)
    {
        ConfigurationRules.Entity(builder, "ai_conversations", mutable: true);
        builder.Property(conversation => conversation.UserId).IsRequired();
        builder.Property(conversation => conversation.TicketId);
        builder.Property(conversation => conversation.Title).HasMaxLength(160);
        builder.Property(conversation => conversation.LastMessageAt);
        builder.HasOne<User>().WithMany().HasForeignKey(conversation => conversation.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Ticket>().WithMany().HasForeignKey(conversation => conversation.TicketId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(conversation => conversation.Messages).WithOne().HasForeignKey(message => message.ConversationId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(conversation => conversation.Messages).HasField("messages").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(conversation => new { conversation.UserId, conversation.UpdatedAt }).IsDescending(false, true).HasDatabaseName("ix_conversations_owner_updated");
        builder.ToTable("ai_conversations", table => table.HasCheckConstraint("ck_conversations_last_message", "last_message_at IS NULL OR (last_message_at >= created_at AND last_message_at <= updated_at)"));
    }
}
