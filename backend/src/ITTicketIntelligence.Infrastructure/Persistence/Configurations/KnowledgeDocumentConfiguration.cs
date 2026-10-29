using ITTicketIntelligence.Domain.Enums;
using ITTicketIntelligence.Domain.Identity;
using ITTicketIntelligence.Domain.Knowledge;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ITTicketIntelligence.Infrastructure.Persistence.Configurations;

internal sealed class KnowledgeDocumentConfiguration : IEntityTypeConfiguration<KnowledgeDocument>
{
    public void Configure(EntityTypeBuilder<KnowledgeDocument> builder)
    {
        ConfigurationRules.Entity(builder, "knowledge_documents", mutable: true);
        builder.Property(document => document.Title).HasMaxLength(160).IsRequired();
        builder.Property(document => document.Content).HasMaxLength(100000).IsRequired();
        builder.Property(document => document.Source).HasMaxLength(512).IsRequired();
        builder.Property(document => document.Category).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(document => document.CreatedById);
        builder.Property(document=>document.IsRestricted).HasDefaultValue(false);
        builder.Property(document => document.Version).IsConcurrencyToken();
        builder.Property(document => document.Kind).HasConversion<string>().HasMaxLength(32).HasDefaultValue(KnowledgeDocumentKind.Document).IsRequired();
        builder.Property(document => document.Format).HasConversion<string>().HasMaxLength(16).HasDefaultValue(KnowledgeContentFormat.PlainText).IsRequired();
        builder.Property(document => document.CleanedContent).HasMaxLength(100000);
        builder.Property(document => document.ProcessingVersion).HasMaxLength(64);
        builder.Property(document => document.ProcessedAt);
        builder.HasMany(document => document.Chunks).WithOne().HasForeignKey(chunk => chunk.DocumentId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(document => document.Chunks).HasField("chunks").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(document => new { document.Kind, document.UpdatedAt, document.Id }).IsDescending(false, true, false).HasDatabaseName("ix_knowledge_kind_updated_id");
        builder.HasOne<User>().WithMany().HasForeignKey(document => document.CreatedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(document => document.Source).IsUnique().HasDatabaseName("ux_knowledge_documents_source");
        builder.HasIndex(document => new { document.Category, document.UpdatedAt }).IsDescending(false, true).HasDatabaseName("ix_knowledge_category_updated");
        builder.ToTable("knowledge_documents", table =>
        {
            table.HasCheckConstraint("ck_knowledge_text", ConfigurationRules.NotBlank("title", "content", "source"));
            table.HasCheckConstraint("ck_knowledge_category", ConfigurationRules.EnumValues<TicketCategory>("category"));
            table.HasCheckConstraint("ck_knowledge_version", "version >= 1");
            table.HasCheckConstraint("ck_knowledge_kind", ConfigurationRules.EnumValues<KnowledgeDocumentKind>("kind"));
            table.HasCheckConstraint("ck_knowledge_format", ConfigurationRules.EnumValues<KnowledgeContentFormat>("format"));
            table.HasCheckConstraint("ck_knowledge_preparation", "(cleaned_content IS NULL AND processing_version IS NULL AND processed_at IS NULL) OR (cleaned_content IS NOT NULL AND cleaned_content ~ '[^[:space:]]' AND processing_version IS NOT NULL AND processing_version ~ '[^[:space:]]' AND processed_at IS NOT NULL AND processed_at >= updated_at)");
        });
    }
}
