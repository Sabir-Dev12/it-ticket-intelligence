using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ITTicketIntelligence.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TicketIndividualAssignee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "assigned_user_id",
                schema: "app",
                table: "tickets",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_tickets_assignee_status",
                schema: "app",
                table: "tickets",
                columns: new[] { "assigned_user_id", "status" },
                filter: "assigned_user_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_tickets_users_assigned_user_id",
                schema: "app",
                table: "tickets",
                column: "assigned_user_id",
                principalSchema: "app",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_tickets_users_assigned_user_id",
                schema: "app",
                table: "tickets");

            migrationBuilder.DropIndex(
                name: "ix_tickets_assignee_status",
                schema: "app",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "assigned_user_id",
                schema: "app",
                table: "tickets");
        }
    }
}
