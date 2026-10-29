using ITTicketIntelligence.Domain.Knowledge;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ITTicketIntelligence.Infrastructure.Persistence.Configurations;

internal sealed class KnowledgeChunkConfiguration : IEntityTypeConfiguration<KnowledgeChunk>
{
    public void Configure(EntityTypeBuilder<KnowledgeChunk> builder)
    {
        ConfigurationRules.Entity(builder, "knowledge_chunks");
        builder.Property(chunk => chunk.DocumentId).IsRequired();
        builder.Property(chunk => chunk.DocumentVersion).IsRequired();
        builder.Property(chunk => chunk.Position).IsRequired();
        builder.Property(chunk => chunk.Content).HasMaxLength(4000).IsRequired();
        builder.Property(chunk => chunk.StartOffset).IsRequired();
        builder.Property(chunk => chunk.EndOffset).IsRequired();
        builder.Property(chunk => chunk.ContentHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(chunk => new { chunk.DocumentId, chunk.Position }).IsUnique().HasDatabaseName("ux_knowledge_chunks_document_position");
        builder.ToTable("knowledge_chunks", table =>
        {
            table.HasCheckConstraint("ck_knowledge_chunks_version", "document_version >= 1 AND position >= 0");
            // .NET offsets count UTF-16 code units; PostgreSQL char_length counts Unicode code points.
            table.HasCheckConstraint("ck_knowledge_chunks_offsets", "start_offset >= 0 AND end_offset > start_offset");
            table.HasCheckConstraint("ck_knowledge_chunks_content", ConfigurationRules.NotBlank("content"));
            table.HasCheckConstraint("ck_knowledge_chunks_hash", "content_hash ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("ck_knowledge_chunks_time", "updated_at = created_at");
        });
    }
}
