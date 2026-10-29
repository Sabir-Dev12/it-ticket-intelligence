using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ITTicketIntelligence.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AssistantConversations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "assistant_turns",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assistant_message_id = table.Column<Guid>(type: "uuid", nullable: true),
                    analysis_id = table.Column<Guid>(type: "uuid", nullable: true),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    error_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    lease_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_assistant_turns", x => x.id);
                    table.CheckConstraint("ck_assistant_turn_ids", "id <> '00000000-0000-0000-0000-000000000000' AND run_id <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("ck_assistant_turn_state", "(state = 'Pending' AND assistant_message_id IS NULL AND analysis_id IS NULL AND error_code IS NULL) OR (state = 'Completed' AND assistant_message_id IS NOT NULL AND analysis_id = run_id AND error_code IS NULL) OR (state = 'Failed' AND assistant_message_id IS NOT NULL AND analysis_id IS NULL AND error_code IN ('Interrupted','TicketChanged','TicketUnavailable','Timeout','DependencyUnavailable'))");
                    table.CheckConstraint("ck_assistant_turn_time", "updated_at >= created_at AND lease_expires_at > created_at");
                    table.ForeignKey(
                        name: "FK_assistant_turns_ai_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalSchema: "app",
                        principalTable: "ai_conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_assistant_turns_ai_messages_assistant_message_id",
                        column: x => x.assistant_message_id,
                        principalSchema: "app",
                        principalTable: "ai_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_assistant_turns_ai_messages_user_message_id",
                        column: x => x.user_message_id,
                        principalSchema: "app",
                        principalTable: "ai_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_assistant_turns_ticket_agent_analyses_analysis_id",
                        column: x => x.analysis_id,
                        principalSchema: "app",
                        principalTable: "ticket_agent_analyses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_assistant_turns_analysis_id",
                schema: "app",
                table: "assistant_turns",
                column: "analysis_id");

            migrationBuilder.CreateIndex(
                name: "IX_assistant_turns_assistant_message_id",
                schema: "app",
                table: "assistant_turns",
                column: "assistant_message_id");

            migrationBuilder.CreateIndex(
                name: "IX_assistant_turns_conversation_id_created_at",
                schema: "app",
                table: "assistant_turns",
                columns: new[] { "conversation_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_assistant_turns_run_id",
                schema: "app",
                table: "assistant_turns",
                column: "run_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_assistant_turns_user_message_id",
                schema: "app",
                table: "assistant_turns",
                column: "user_message_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assistant_turns",
                schema: "app");
        }
    }
}
