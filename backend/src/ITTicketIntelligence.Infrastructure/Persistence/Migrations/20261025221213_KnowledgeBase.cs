using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ITTicketIntelligence.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class KnowledgeBase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cleaned_content",
                schema: "app",
                table: "knowledge_documents",
                type: "character varying(100000)",
                maxLength: 100000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "format",
                schema: "app",
                table: "knowledge_documents",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "PlainText");

            migrationBuilder.AddColumn<string>(
                name: "kind",
                schema: "app",
                table: "knowledge_documents",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Document");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "processed_at",
                schema: "app",
                table: "knowledge_documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "processing_version",
                schema: "app",
                table: "knowledge_documents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "knowledge_chunks",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_version = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    content = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    start_offset = table.Column<int>(type: "integer", nullable: false),
                    end_offset = table.Column<int>(type: "integer", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_chunks", x => x.id);
                    table.CheckConstraint("ck_knowledge_chunks_content", "content ~ '[^[:space:]]'");
                    table.CheckConstraint("ck_knowledge_chunks_hash", "content_hash ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_knowledge_chunks_id", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_knowledge_chunks_offsets", "start_offset >= 0 AND end_offset > start_offset");
                    table.CheckConstraint("ck_knowledge_chunks_time", "updated_at = created_at");
                    table.CheckConstraint("ck_knowledge_chunks_timestamps", "updated_at >= created_at");
                    table.CheckConstraint("ck_knowledge_chunks_version", "document_version >= 1 AND position >= 0");
                    table.ForeignKey(
                        name: "FK_knowledge_chunks_knowledge_documents_document_id",
                        column: x => x.document_id,
                        principalSchema: "app",
                        principalTable: "knowledge_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_kind_updated_id",
                schema: "app",
                table: "knowledge_documents",
                columns: new[] { "kind", "updated_at", "id" },
                descending: new[] { false, true, false });

            migrationBuilder.AddCheckConstraint(
                name: "ck_knowledge_format",
                schema: "app",
                table: "knowledge_documents",
                sql: "format IN ('PlainText', 'Markdown')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_knowledge_kind",
                schema: "app",
                table: "knowledge_documents",
                sql: "kind IN ('Document', 'Article', 'TroubleshootingGuide', 'Faq', 'Procedure')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_knowledge_preparation",
                schema: "app",
                table: "knowledge_documents",
                sql: "(cleaned_content IS NULL AND processing_version IS NULL AND processed_at IS NULL) OR (cleaned_content IS NOT NULL AND cleaned_content ~ '[^[:space:]]' AND processing_version IS NOT NULL AND processing_version ~ '[^[:space:]]' AND processed_at IS NOT NULL AND processed_at >= updated_at)");

            migrationBuilder.CreateIndex(
                name: "ux_knowledge_chunks_document_position",
                schema: "app",
                table: "knowledge_chunks",
                columns: new[] { "document_id", "position" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "knowledge_chunks",
                schema: "app");

            migrationBuilder.DropIndex(
                name: "ix_knowledge_kind_updated_id",
                schema: "app",
                table: "knowledge_documents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_knowledge_format",
                schema: "app",
                table: "knowledge_documents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_knowledge_kind",
                schema: "app",
                table: "knowledge_documents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_knowledge_preparation",
                schema: "app",
                table: "knowledge_documents");

            migrationBuilder.DropColumn(
                name: "cleaned_content",
                schema: "app",
                table: "knowledge_documents");

            migrationBuilder.DropColumn(
                name: "format",
                schema: "app",
                table: "knowledge_documents");

            migrationBuilder.DropColumn(
                name: "kind",
                schema: "app",
                table: "knowledge_documents");

            migrationBuilder.DropColumn(
                name: "processed_at",
                schema: "app",
                table: "knowledge_documents");

            migrationBuilder.DropColumn(
                name: "processing_version",
                schema: "app",
                table: "knowledge_documents");
        }
    }
}
