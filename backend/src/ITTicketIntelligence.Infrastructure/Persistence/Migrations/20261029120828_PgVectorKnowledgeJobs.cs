using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ITTicketIntelligence.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PgVectorKnowledgeJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE EXTENSION IF NOT EXISTS vector WITH SCHEMA public;
                ALTER TABLE app.rag_vectors ADD COLUMN embedding_vector public.vector GENERATED ALWAYS AS (embedding::public.vector) STORED;
                CREATE TABLE app.knowledge_index_jobs (
                    id uuid PRIMARY KEY, document_id uuid NOT NULL REFERENCES app.knowledge_documents(id) ON DELETE CASCADE,
                    profile varchar(256) NOT NULL, state varchar(32) NOT NULL CHECK(state IN ('Queued','Running','Succeeded','Failed','RequiresReindex')),
                    attempts integer NOT NULL CHECK(attempts BETWEEN 0 AND 3), created_at timestamptz NOT NULL DEFAULT now(), lease_until timestamptz NULL);
                CREATE INDEX ix_knowledge_jobs_claim ON app.knowledge_index_jobs(state,created_at);
                """);

            migrationBuilder.AddColumn<bool>(
                name: "is_restricted",
                schema: "app",
                table: "knowledge_documents",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE app.knowledge_index_jobs; ALTER TABLE app.rag_vectors DROP COLUMN embedding_vector;");

            migrationBuilder.DropColumn(
                name: "is_restricted",
                schema: "app",
                table: "knowledge_documents");
        }
    }
}
