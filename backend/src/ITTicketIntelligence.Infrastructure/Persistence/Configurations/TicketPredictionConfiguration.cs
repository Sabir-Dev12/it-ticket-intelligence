using ITTicketIntelligence.Domain.Enums;
using ITTicketIntelligence.Domain.Identity;
using ITTicketIntelligence.Domain.Tickets;
using ITTicketIntelligence.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ITTicketIntelligence.Infrastructure.Persistence.Configurations;

internal sealed class TicketPredictionConfiguration : IEntityTypeConfiguration<TicketPrediction>
{
    public void Configure(EntityTypeBuilder<TicketPrediction> builder)
    {
        ConfigurationRules.Entity(builder, "ticket_predictions");
        builder.Property(prediction => prediction.TicketId).IsRequired();
        builder.Property(prediction => prediction.Type).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(prediction => prediction.Confidence).HasConversion(confidence => confidence.Value, value => new PredictionConfidence(value)).IsRequired();
        builder.Property(prediction => prediction.ModelVersion).HasMaxLength(100).IsRequired();
        builder.Property(prediction => prediction.InputRevision).IsRequired();
        builder.Property(prediction => prediction.PredictedCategory).HasConversion<string>().HasMaxLength(32);
        builder.Property(prediction => prediction.PredictedPriority).HasConversion<string>().HasMaxLength(16);
        builder.Property(prediction => prediction.RecommendedTeamId);
        builder.Property(prediction => prediction.RecommendedTeamName).HasMaxLength(32);
        builder.Property(prediction => prediction.AnalysisId);
        builder.Property(prediction => prediction.ReviewThreshold);
        builder.Property(prediction => prediction.NeedsHumanReview).IsRequired();
        builder.HasIndex(prediction => new { prediction.AnalysisId, prediction.Type }).IsUnique()
            .HasDatabaseName("ix_predictions_analysis_type");
        builder.HasOne<Team>().WithMany().HasForeignKey(prediction => prediction.RecommendedTeamId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(prediction => new { prediction.TicketId, prediction.Type, prediction.CreatedAt }).IsDescending(false, false, true).HasDatabaseName("ix_predictions_ticket_type_created");
        builder.ToTable("ticket_predictions", table =>
        {
            table.HasCheckConstraint("ck_predictions_type", ConfigurationRules.EnumValues<PredictionType>("type"));
            table.HasCheckConstraint("ck_predictions_category", ConfigurationRules.EnumValues<TicketCategory>("predicted_category"));
            table.HasCheckConstraint("ck_predictions_priority", ConfigurationRules.EnumValues<TicketPriority>("predicted_priority"));
            table.HasCheckConstraint("ck_predictions_confidence", "confidence >= 0 AND confidence <= 1");
            table.HasCheckConstraint("ck_predictions_revision", "input_revision >= 1");
            table.HasCheckConstraint("ck_predictions_model", ConfigurationRules.NotBlank("model_version"));
            table.HasCheckConstraint("ck_predictions_result", "(type = 'Category' AND predicted_category IS NOT NULL AND predicted_priority IS NULL AND recommended_team_id IS NULL AND recommended_team_name IS NULL) OR (type = 'Priority' AND predicted_category IS NULL AND predicted_priority IS NOT NULL AND recommended_team_id IS NULL AND recommended_team_name IS NULL) OR (type = 'TeamRouting' AND predicted_category IS NULL AND predicted_priority IS NULL AND ((recommended_team_id IS NOT NULL AND recommended_team_name IS NULL AND review_threshold IS NULL) OR (recommended_team_id IS NULL AND review_threshold IS NOT NULL AND ((needs_human_review AND recommended_team_name IS NULL) OR (NOT needs_human_review AND recommended_team_name IS NOT NULL AND btrim(recommended_team_name) <> '')))))");
            table.HasCheckConstraint("ck_predictions_review", "(review_threshold IS NULL AND NOT needs_human_review) OR (review_threshold IS NOT NULL AND review_threshold > 0 AND review_threshold <= 1 AND needs_human_review = (confidence < review_threshold))");
            table.HasCheckConstraint("ck_predictions_analysis", "analysis_id IS NULL OR analysis_id <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("ck_predictions_immutable_time", "updated_at = created_at");
        });
    }
}
