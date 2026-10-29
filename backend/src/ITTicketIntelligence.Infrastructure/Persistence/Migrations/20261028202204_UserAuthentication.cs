using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ITTicketIntelligence.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UserAuthentication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_users_role",
                schema: "app",
                table: "users");

            migrationBuilder.AddColumn<Guid>(
                name: "auth_version",
                schema: "app",
                table: "users",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "lockout_until",
                schema: "app",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "login_failures",
                schema: "app",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "password_hash",
                schema: "app",
                table: "users",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_role",
                schema: "app",
                table: "users",
                sql: "role IN ('User', 'Agent', 'Admin', 'Support')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_users_role",
                schema: "app",
                table: "users");

            migrationBuilder.DropColumn(
                name: "auth_version",
                schema: "app",
                table: "users");

            migrationBuilder.DropColumn(
                name: "lockout_until",
                schema: "app",
                table: "users");

            migrationBuilder.DropColumn(
                name: "login_failures",
                schema: "app",
                table: "users");

            migrationBuilder.DropColumn(
                name: "password_hash",
                schema: "app",
                table: "users");

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_role",
                schema: "app",
                table: "users",
                sql: "role IN ('User', 'Agent', 'Admin')");
        }
    }
}
