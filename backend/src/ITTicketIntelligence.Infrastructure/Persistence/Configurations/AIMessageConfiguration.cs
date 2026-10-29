using ITTicketIntelligence.Domain.Conversations;
using ITTicketIntelligence.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ITTicketIntelligence.Infrastructure.Persistence.Configurations;

internal sealed class AIMessageConfiguration : IEntityTypeConfiguration<AIMessage>
{
    public void Configure(EntityTypeBuilder<AIMessage> builder)
    {
        ConfigurationRules.Entity(builder, "ai_messages");
        builder.Property(message => message.ConversationId).IsRequired();
        builder.Property(message => message.Role).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(message => message.Content).HasMaxLength(16000).IsRequired();
        builder.Property(message => message.ToolName).HasMaxLength(128);
        builder.Property(message => message.ToolCallId).HasMaxLength(128);
        builder.Property<int>("Position").IsRequired();
        builder.HasIndex("ConversationId", "Position").IsUnique().HasDatabaseName("ux_messages_conversation_position");
        builder.ToTable("ai_messages", table =>
        {
            table.HasCheckConstraint("ck_messages_role", ConfigurationRules.EnumValues<AIMessageRole>("role"));
            table.HasCheckConstraint("ck_messages_content", ConfigurationRules.NotBlank("content"));
            table.HasCheckConstraint("ck_messages_position", "position >= 1");
            table.HasCheckConstraint("ck_messages_tool", "(role = 'Tool' AND tool_name IS NOT NULL AND tool_name ~ '[^[:space:]]' AND tool_call_id IS NOT NULL AND tool_call_id ~ '[^[:space:]]') OR (role <> 'Tool' AND tool_name IS NULL AND tool_call_id IS NULL)");
            table.HasCheckConstraint("ck_messages_immutable_time", "updated_at = created_at");
        });
    }
}
