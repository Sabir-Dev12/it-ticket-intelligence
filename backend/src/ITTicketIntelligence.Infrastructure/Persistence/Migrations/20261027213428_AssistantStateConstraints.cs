using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ITTicketIntelligence.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AssistantStateConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_assistant_turn_state",
                schema: "app",
                table: "assistant_turns");

            migrationBuilder.AddCheckConstraint(
                name: "ck_assistant_turn_state",
                schema: "app",
                table: "assistant_turns",
                sql: "(state = 'Pending' AND assistant_message_id IS NULL AND analysis_id IS NULL AND error_code IS NULL) OR (state = 'Completed' AND assistant_message_id IS NOT NULL AND analysis_id IS NOT NULL AND analysis_id = run_id AND error_code IS NULL) OR (state = 'Failed' AND assistant_message_id IS NOT NULL AND analysis_id IS NULL AND error_code IS NOT NULL AND error_code IN ('Interrupted','TicketChanged','TicketUnavailable','Timeout','DependencyUnavailable'))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_assistant_turn_state",
                schema: "app",
                table: "assistant_turns");

            migrationBuilder.AddCheckConstraint(
                name: "ck_assistant_turn_state",
                schema: "app",
                table: "assistant_turns",
                sql: "(state = 'Pending' AND assistant_message_id IS NULL AND analysis_id IS NULL AND error_code IS NULL) OR (state = 'Completed' AND assistant_message_id IS NOT NULL AND analysis_id = run_id AND error_code IS NULL) OR (state = 'Failed' AND assistant_message_id IS NOT NULL AND analysis_id IS NULL AND error_code IN ('Interrupted','TicketChanged','TicketUnavailable','Timeout','DependencyUnavailable'))");
        }
    }
}
