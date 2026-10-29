using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ITTicketIntelligence.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RagVectors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rag_vectors",
                schema: "app",
                columns: table => new
                {
                    chunk_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    dimensions = table.Column<int>(type: "integer", nullable: false),
                    embedding = table.Column<float[]>(type: "real[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rag_vectors", x => new { x.chunk_id, x.profile });
                    table.CheckConstraint("ck_rag_vectors_dimensions", "dimensions BETWEEN 1 AND 4096 AND array_ndims(embedding) = 1 AND array_lower(embedding, 1) = 1 AND cardinality(embedding) = dimensions");
                    table.CheckConstraint("ck_rag_vectors_profile", "btrim(profile) <> ''");
                    table.CheckConstraint("ck_rag_vectors_time", "updated_at = created_at");
                    table.CheckConstraint("ck_rag_vectors_values", "array_position(embedding, NULL) IS NULL AND array_position(embedding, 'NaN'::real) IS NULL AND array_position(embedding, 'Infinity'::real) IS NULL AND array_position(embedding, '-Infinity'::real) IS NULL");
                    table.ForeignKey(
                        name: "FK_rag_vectors_knowledge_chunks_chunk_id",
                        column: x => x.chunk_id,
                        principalSchema: "app",
                        principalTable: "knowledge_chunks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_rag_vectors_profile_dimensions",
                schema: "app",
                table: "rag_vectors",
                columns: new[] { "profile", "dimensions" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rag_vectors",
                schema: "app");
        }
    }
}
