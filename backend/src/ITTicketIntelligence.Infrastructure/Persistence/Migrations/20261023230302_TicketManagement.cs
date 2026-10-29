using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ITTicketIntelligence.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TicketManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "ticket_reference_sequence",
                schema: "app",
                startValue: 1000L);

            // Existing references may predate the server-side sequence.
            migrationBuilder.Sql("""
                SELECT setval('app.ticket_reference_sequence',
                    GREATEST(1000, COALESCE((
                        SELECT MAX(substring(reference FROM '^IT-([0-9]{1,18})$')::bigint)
                        FROM app.tickets
                        WHERE reference ~ '^IT-[0-9]{1,18}$'
                    ), 999) + 1), false);
                """);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "archived_at",
                schema: "app",
                table: "tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_archived",
                schema: "app",
                table: "tickets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_tickets_archive_created_id",
                schema: "app",
                table: "tickets",
                columns: new[] { "is_archived", "created_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.AddCheckConstraint(
                name: "ck_tickets_archive",
                schema: "app",
                table: "tickets",
                sql: "(NOT is_archived AND archived_at IS NULL) OR (is_archived AND archived_at IS NOT NULL AND archived_at >= created_at AND archived_at <= updated_at)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_tickets_archive_created_id",
                schema: "app",
                table: "tickets");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tickets_archive",
                schema: "app",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "archived_at",
                schema: "app",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "is_archived",
                schema: "app",
                table: "tickets");

            migrationBuilder.DropSequence(
                name: "ticket_reference_sequence",
                schema: "app");
        }
    }
}
