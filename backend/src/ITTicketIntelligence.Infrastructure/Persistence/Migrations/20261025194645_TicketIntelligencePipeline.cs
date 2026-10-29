using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ITTicketIntelligence.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TicketIntelligencePipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_predictions_result",
                schema: "app",
                table: "ticket_predictions");

            migrationBuilder.AddColumn<Guid>(
                name: "analysis_id",
                schema: "app",
                table: "ticket_predictions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "needs_human_review",
                schema: "app",
                table: "ticket_predictions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "recommended_team_name",
                schema: "app",
                table: "ticket_predictions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "review_threshold",
                schema: "app",
                table: "ticket_predictions",
                type: "double precision",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_predictions_analysis_type",
                schema: "app",
                table: "ticket_predictions",
                columns: new[] { "analysis_id", "type" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_predictions_analysis",
                schema: "app",
                table: "ticket_predictions",
                sql: "analysis_id IS NULL OR analysis_id <> '00000000-0000-0000-0000-000000000000'::uuid");

            migrationBuilder.AddCheckConstraint(
                name: "ck_predictions_result",
                schema: "app",
                table: "ticket_predictions",
                sql: "(type = 'Category' AND predicted_category IS NOT NULL AND predicted_priority IS NULL AND recommended_team_id IS NULL AND recommended_team_name IS NULL) OR (type = 'Priority' AND predicted_category IS NULL AND predicted_priority IS NOT NULL AND recommended_team_id IS NULL AND recommended_team_name IS NULL) OR (type = 'TeamRouting' AND predicted_category IS NULL AND predicted_priority IS NULL AND ((recommended_team_id IS NOT NULL AND recommended_team_name IS NULL AND review_threshold IS NULL) OR (recommended_team_id IS NULL AND review_threshold IS NOT NULL AND ((needs_human_review AND recommended_team_name IS NULL) OR (NOT needs_human_review AND recommended_team_name IS NOT NULL AND btrim(recommended_team_name) <> '')))))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_predictions_review",
                schema: "app",
                table: "ticket_predictions",
                sql: "(review_threshold IS NULL AND NOT needs_human_review) OR (review_threshold IS NOT NULL AND review_threshold > 0 AND review_threshold <= 1 AND needs_human_review = (confidence < review_threshold))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_predictions_analysis_type",
                schema: "app",
                table: "ticket_predictions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_predictions_analysis",
                schema: "app",
                table: "ticket_predictions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_predictions_result",
                schema: "app",
                table: "ticket_predictions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_predictions_review",
                schema: "app",
                table: "ticket_predictions");

            migrationBuilder.DropColumn(
                name: "analysis_id",
                schema: "app",
                table: "ticket_predictions");

            migrationBuilder.DropColumn(
                name: "needs_human_review",
                schema: "app",
                table: "ticket_predictions");

            migrationBuilder.DropColumn(
                name: "recommended_team_name",
                schema: "app",
                table: "ticket_predictions");

            migrationBuilder.DropColumn(
                name: "review_threshold",
                schema: "app",
                table: "ticket_predictions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_predictions_result",
                schema: "app",
                table: "ticket_predictions",
                sql: "(type = 'Category' AND predicted_category IS NOT NULL AND predicted_priority IS NULL AND recommended_team_id IS NULL) OR (type = 'Priority' AND predicted_category IS NULL AND predicted_priority IS NOT NULL AND recommended_team_id IS NULL) OR (type = 'TeamRouting' AND predicted_category IS NULL AND predicted_priority IS NULL AND recommended_team_id IS NOT NULL)");
        }
    }
}
