using ITTicketIntelligence.Domain.Knowledge;
using ITTicketIntelligence.Infrastructure.Persistence.Rag;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ITTicketIntelligence.Infrastructure.Persistence.Configurations;

internal sealed class RagVectorConfiguration : IEntityTypeConfiguration<RagVectorRecord>
{
    public void Configure(EntityTypeBuilder<RagVectorRecord> builder)
    {
        builder.ToTable("rag_vectors", table =>
        {
            table.HasCheckConstraint("ck_rag_vectors_dimensions", "dimensions BETWEEN 1 AND 4096 AND array_ndims(embedding) = 1 AND array_lower(embedding, 1) = 1 AND cardinality(embedding) = dimensions");
            table.HasCheckConstraint("ck_rag_vectors_values", "array_position(embedding, NULL) IS NULL AND array_position(embedding, 'NaN'::real) IS NULL AND array_position(embedding, 'Infinity'::real) IS NULL AND array_position(embedding, '-Infinity'::real) IS NULL");
            table.HasCheckConstraint("ck_rag_vectors_profile", "btrim(profile) <> ''");
            table.HasCheckConstraint("ck_rag_vectors_time", "updated_at = created_at");
        });
        builder.HasKey(row => new { row.ChunkId, row.Profile });
        builder.Property(row => row.ChunkId).ValueGeneratedNever();
        builder.Property(row => row.Profile).HasMaxLength(256);
        builder.Property(row => row.Dimensions).IsRequired();
        builder.Property(row => row.Embedding).HasColumnType("real[]").IsRequired();
        builder.Property(row => row.CreatedAt).IsRequired();
        builder.Property(row => row.UpdatedAt).IsRequired();
        builder.HasOne<KnowledgeChunk>().WithMany().HasForeignKey(row => row.ChunkId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(row => new { row.Profile, row.Dimensions }).HasDatabaseName("ix_rag_vectors_profile_dimensions");
    }
}
