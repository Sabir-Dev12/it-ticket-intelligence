using ITTicketIntelligence.Domain.Conversations;
using ITTicketIntelligence.Infrastructure.Persistence.Agents;
using ITTicketIntelligence.Infrastructure.Persistence.Assistant;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ITTicketIntelligence.Infrastructure.Persistence.Configurations;

internal sealed class AssistantTurnConfiguration : IEntityTypeConfiguration<AssistantTurnRecord>
{
    public void Configure(EntityTypeBuilder<AssistantTurnRecord> b)
    {
        b.ToTable("assistant_turns", t =>
        {
            t.HasCheckConstraint("ck_assistant_turn_ids", "id <> '00000000-0000-0000-0000-000000000000' AND run_id <> '00000000-0000-0000-0000-000000000000'");
            t.HasCheckConstraint("ck_assistant_turn_time", "updated_at >= created_at AND lease_expires_at > created_at");
            t.HasCheckConstraint("ck_assistant_turn_state", "(state = 'Pending' AND assistant_message_id IS NULL AND analysis_id IS NULL AND error_code IS NULL) OR (state = 'Completed' AND assistant_message_id IS NOT NULL AND analysis_id IS NOT NULL AND analysis_id = run_id AND error_code IS NULL) OR (state = 'Failed' AND assistant_message_id IS NOT NULL AND analysis_id IS NULL AND error_code IS NOT NULL AND error_code IN ('Interrupted','TicketChanged','TicketUnavailable','Timeout','DependencyUnavailable'))");
        });
        b.HasKey(t => t.Id);
        b.Property(t => t.Id).ValueGeneratedNever();
        b.Property(t => t.State).HasMaxLength(16).IsRequired();
        b.Property(t => t.ErrorCode).HasMaxLength(32);
        b.Property<uint>("xmin").IsRowVersion();
        b.HasOne<AIConversation>().WithMany().HasForeignKey(t => t.ConversationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AIMessage>().WithMany().HasForeignKey(t => t.UserMessageId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AIMessage>().WithMany().HasForeignKey(t => t.AssistantMessageId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AgentAnalysisRecord>().WithMany().HasForeignKey(t => t.AnalysisId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(t => t.RunId).IsUnique();
        b.HasIndex(t => t.UserMessageId).IsUnique();
        b.HasIndex(t => new { t.ConversationId, t.CreatedAt });
    }
}
