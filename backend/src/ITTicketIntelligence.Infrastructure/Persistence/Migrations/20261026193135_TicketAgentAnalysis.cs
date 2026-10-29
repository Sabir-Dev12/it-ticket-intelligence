using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ITTicketIntelligence.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TicketAgentAnalysis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ticket_agent_analyses",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_revision = table.Column<int>(type: "integer", nullable: false),
                    result_json = table.Column<string>(type: "jsonb", nullable: false),
                    result_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    model = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_agent_analyses", x => x.id);
                    table.CheckConstraint("ck_agent_provider", "btrim(provider) <> '' AND btrim(model) <> ''");
                    table.CheckConstraint("ck_agent_result", "jsonb_typeof(result_json) = 'object' AND result_hash ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_agent_revision", "ticket_revision > 0");
                    table.CheckConstraint("ck_agent_time", "updated_at = created_at");
                    table.ForeignKey(
                        name: "FK_ticket_agent_analyses_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalSchema: "app",
                        principalTable: "tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agent_ticket_created",
                schema: "app",
                table: "ticket_agent_analyses",
                columns: new[] { "ticket_id", "created_at" },
                descending: new[] { false, true });
            migrationBuilder.Sql("CREATE TRIGGER protect_history BEFORE UPDATE OR DELETE OR TRUNCATE ON app.ticket_agent_analyses FOR EACH STATEMENT EXECUTE FUNCTION app.reject_history_mutation();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ticket_agent_analyses",
                schema: "app");
        }
    }
}
