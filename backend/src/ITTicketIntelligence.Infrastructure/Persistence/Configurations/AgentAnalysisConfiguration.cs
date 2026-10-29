using ITTicketIntelligence.Domain.Tickets;
using ITTicketIntelligence.Infrastructure.Persistence.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ITTicketIntelligence.Infrastructure.Persistence.Configurations;

internal sealed class AgentAnalysisConfiguration : IEntityTypeConfiguration<AgentAnalysisRecord>
{
    public void Configure(EntityTypeBuilder<AgentAnalysisRecord> builder)
    {
        builder.ToTable("ticket_agent_analyses", t =>
        {
            t.HasCheckConstraint("ck_agent_revision", "ticket_revision > 0");
            t.HasCheckConstraint("ck_agent_result", "jsonb_typeof(result_json) = 'object' AND result_hash ~ '^[0-9a-f]{64}$'");
            t.HasCheckConstraint("ck_agent_time", "updated_at = created_at");
            t.HasCheckConstraint("ck_agent_provider", "btrim(provider) <> '' AND btrim(model) <> ''");
        });
        builder.HasKey(r => r.Id);
        builder.Property(r => r.ResultJson).HasColumnType("jsonb").IsRequired();
        builder.Property(r => r.ResultHash).HasMaxLength(64).IsRequired();
        builder.Property(r => r.Provider).HasMaxLength(64).IsRequired();
        builder.Property(r => r.Model).HasMaxLength(256).IsRequired();
        builder.HasOne<Ticket>().WithMany().HasForeignKey(r => r.TicketId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(r => new { r.TicketId, r.CreatedAt }).IsDescending(false, true).HasDatabaseName("ix_agent_ticket_created");
    }
}
