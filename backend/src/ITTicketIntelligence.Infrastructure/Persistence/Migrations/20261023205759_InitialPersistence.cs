using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ITTicketIntelligence.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "app");

            migrationBuilder.CreateTable(
                name: "teams",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    normalized_name = table.Column<string>(type: "text", nullable: true, computedColumnSql: "lower(btrim(name))", stored: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_teams", x => x.id);
                    table.CheckConstraint("ck_teams_id", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_teams_name", "name ~ '[^[:space:]]'");
                    table.CheckConstraint("ck_teams_timestamps", "updated_at >= created_at");
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                    table.CheckConstraint("ck_users_email_normalized", "email = lower(btrim(email))");
                    table.CheckConstraint("ck_users_id", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_users_role", "role IN ('User', 'Agent', 'Admin')");
                    table.CheckConstraint("ck_users_text", "name ~ '[^[:space:]]' AND email ~ '[^[:space:]]'");
                    table.CheckConstraint("ck_users_timestamps", "updated_at >= created_at");
                });

            migrationBuilder.CreateTable(
                name: "audit_logs",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    details = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_logs", x => x.id);
                    table.CheckConstraint("ck_audit_correlation", "correlation_id IS NULL OR correlation_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_audit_immutable_time", "updated_at = created_at");
                    table.CheckConstraint("ck_audit_logs_id", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_audit_logs_timestamps", "updated_at >= created_at");
                    table.CheckConstraint("ck_audit_target", "entity_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_audit_text", "action ~ '[^[:space:]]' AND entity_type ~ '[^[:space:]]'");
                    table.ForeignKey(
                        name: "FK_audit_logs_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "knowledge_documents",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    content = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: false),
                    source = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_documents", x => x.id);
                    table.CheckConstraint("ck_knowledge_category", "category IN ('Billing', 'Network', 'Hardware', 'Software', 'Account', 'Security', 'Database', 'Application', 'Other')");
                    table.CheckConstraint("ck_knowledge_documents_id", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_knowledge_documents_timestamps", "updated_at >= created_at");
                    table.CheckConstraint("ck_knowledge_text", "title ~ '[^[:space:]]' AND content ~ '[^[:space:]]' AND source ~ '[^[:space:]]'");
                    table.CheckConstraint("ck_knowledge_version", "version >= 1");
                    table.ForeignKey(
                        name: "FK_knowledge_documents_users_created_by_id",
                        column: x => x.created_by_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "team_members",
                schema: "app",
                columns: table => new
                {
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_team_members", x => new { x.team_id, x.user_id });
                    table.CheckConstraint("ck_team_members_timestamps", "updated_at = created_at");
                    table.ForeignKey(
                        name: "FK_team_members_teams_team_id",
                        column: x => x.team_id,
                        principalSchema: "app",
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_team_members_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tickets",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    description = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    priority = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    assigned_team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolution = table.Column<string>(type: "character varying(12000)", maxLength: 12000, nullable: true),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tickets", x => x.id);
                    table.CheckConstraint("ck_tickets_category", "category IN ('Billing', 'Network', 'Hardware', 'Software', 'Account', 'Security', 'Database', 'Application', 'Other')");
                    table.CheckConstraint("ck_tickets_classification", "(category IS NULL) = (priority IS NULL)");
                    table.CheckConstraint("ck_tickets_id", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_tickets_lifecycle", "(status IN ('Open', 'InProgress') AND resolution IS NULL AND resolved_at IS NULL AND closed_at IS NULL) OR (status = 'Resolved' AND resolution IS NOT NULL AND resolution ~ '[^[:space:]]' AND resolved_at IS NOT NULL AND closed_at IS NULL) OR (status = 'Closed' AND resolution IS NOT NULL AND resolution ~ '[^[:space:]]' AND resolved_at IS NOT NULL AND closed_at IS NOT NULL)");
                    table.CheckConstraint("ck_tickets_priority", "priority IN ('Low', 'Medium', 'High', 'Critical')");
                    table.CheckConstraint("ck_tickets_reference_normalized", "reference = upper(btrim(reference))");
                    table.CheckConstraint("ck_tickets_resolution_dates", "(resolved_at IS NULL OR (resolved_at >= created_at AND resolved_at <= updated_at)) AND (closed_at IS NULL OR (closed_at >= resolved_at AND closed_at <= updated_at))");
                    table.CheckConstraint("ck_tickets_revision", "revision >= 1");
                    table.CheckConstraint("ck_tickets_status", "status IN ('Open', 'InProgress', 'Resolved', 'Closed')");
                    table.CheckConstraint("ck_tickets_text", "reference ~ '[^[:space:]]' AND title ~ '[^[:space:]]' AND description ~ '[^[:space:]]'");
                    table.CheckConstraint("ck_tickets_timestamps", "updated_at >= created_at");
                    table.ForeignKey(
                        name: "FK_tickets_teams_assigned_team_id",
                        column: x => x.assigned_team_id,
                        principalSchema: "app",
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tickets_users_created_by_id",
                        column: x => x.created_by_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ai_conversations",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_message_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_conversations", x => x.id);
                    table.CheckConstraint("ck_ai_conversations_id", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_ai_conversations_timestamps", "updated_at >= created_at");
                    table.CheckConstraint("ck_conversations_last_message", "last_message_at IS NULL OR (last_message_at >= created_at AND last_message_at <= updated_at)");
                    table.ForeignKey(
                        name: "FK_ai_conversations_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalSchema: "app",
                        principalTable: "tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ai_conversations_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ticket_comments",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_comments", x => x.id);
                    table.CheckConstraint("ck_ticket_comments_id", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_ticket_comments_immutable_time", "updated_at = created_at");
                    table.CheckConstraint("ck_ticket_comments_message", "message ~ '[^[:space:]]'");
                    table.CheckConstraint("ck_ticket_comments_timestamps", "updated_at >= created_at");
                    table.ForeignKey(
                        name: "FK_ticket_comments_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalSchema: "app",
                        principalTable: "tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ticket_comments_users_author_id",
                        column: x => x.author_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ticket_predictions",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    confidence = table.Column<double>(type: "double precision", nullable: false),
                    model_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    input_revision = table.Column<int>(type: "integer", nullable: false),
                    predicted_category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    predicted_priority = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    recommended_team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_predictions", x => x.id);
                    table.CheckConstraint("ck_predictions_category", "predicted_category IN ('Billing', 'Network', 'Hardware', 'Software', 'Account', 'Security', 'Database', 'Application', 'Other')");
                    table.CheckConstraint("ck_predictions_confidence", "confidence >= 0 AND confidence <= 1");
                    table.CheckConstraint("ck_predictions_immutable_time", "updated_at = created_at");
                    table.CheckConstraint("ck_predictions_model", "model_version ~ '[^[:space:]]'");
                    table.CheckConstraint("ck_predictions_priority", "predicted_priority IN ('Low', 'Medium', 'High', 'Critical')");
                    table.CheckConstraint("ck_predictions_result", "(type = 'Category' AND predicted_category IS NOT NULL AND predicted_priority IS NULL AND recommended_team_id IS NULL) OR (type = 'Priority' AND predicted_category IS NULL AND predicted_priority IS NOT NULL AND recommended_team_id IS NULL) OR (type = 'TeamRouting' AND predicted_category IS NULL AND predicted_priority IS NULL AND recommended_team_id IS NOT NULL)");
                    table.CheckConstraint("ck_predictions_revision", "input_revision >= 1");
                    table.CheckConstraint("ck_predictions_type", "type IN ('Category', 'Priority', 'TeamRouting')");
                    table.CheckConstraint("ck_ticket_predictions_id", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_ticket_predictions_timestamps", "updated_at >= created_at");
                    table.ForeignKey(
                        name: "FK_ticket_predictions_teams_recommended_team_id",
                        column: x => x.recommended_team_id,
                        principalSchema: "app",
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ticket_predictions_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalSchema: "app",
                        principalTable: "tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ai_messages",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    content = table.Column<string>(type: "character varying(16000)", maxLength: 16000, nullable: false),
                    tool_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    tool_call_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_messages", x => x.id);
                    table.CheckConstraint("ck_ai_messages_id", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_ai_messages_timestamps", "updated_at >= created_at");
                    table.CheckConstraint("ck_messages_content", "content ~ '[^[:space:]]'");
                    table.CheckConstraint("ck_messages_immutable_time", "updated_at = created_at");
                    table.CheckConstraint("ck_messages_position", "position >= 1");
                    table.CheckConstraint("ck_messages_role", "role IN ('System', 'User', 'Assistant', 'Tool')");
                    table.CheckConstraint("ck_messages_tool", "(role = 'Tool' AND tool_name IS NOT NULL AND tool_name ~ '[^[:space:]]' AND tool_call_id IS NOT NULL AND tool_call_id ~ '[^[:space:]]') OR (role <> 'Tool' AND tool_name IS NULL AND tool_call_id IS NULL)");
                    table.ForeignKey(
                        name: "FK_ai_messages_ai_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalSchema: "app",
                        principalTable: "ai_conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ai_conversations_ticket_id",
                schema: "app",
                table: "ai_conversations",
                column: "ticket_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversations_owner_updated",
                schema: "app",
                table: "ai_conversations",
                columns: new[] { "user_id", "updated_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_messages_conversation_position",
                schema: "app",
                table: "ai_messages",
                columns: new[] { "conversation_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_audit_actor_occurred",
                schema: "app",
                table: "audit_logs",
                columns: new[] { "user_id", "occurred_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_audit_correlation",
                schema: "app",
                table: "audit_logs",
                column: "correlation_id",
                filter: "correlation_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_audit_target_occurred",
                schema: "app",
                table: "audit_logs",
                columns: new[] { "entity_type", "entity_id", "occurred_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_category_updated",
                schema: "app",
                table: "knowledge_documents",
                columns: new[] { "category", "updated_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_documents_created_by_id",
                schema: "app",
                table: "knowledge_documents",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ux_knowledge_documents_source",
                schema: "app",
                table: "knowledge_documents",
                column: "source",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_team_members_user",
                schema: "app",
                table: "team_members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_teams_normalized_name",
                schema: "app",
                table: "teams",
                column: "normalized_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ticket_comments_author_id",
                schema: "app",
                table: "ticket_comments",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_comments_ticket_created",
                schema: "app",
                table: "ticket_comments",
                columns: new[] { "ticket_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_predictions_ticket_type_created",
                schema: "app",
                table: "ticket_predictions",
                columns: new[] { "ticket_id", "type", "created_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_ticket_predictions_recommended_team_id",
                schema: "app",
                table: "ticket_predictions",
                column: "recommended_team_id");

            migrationBuilder.CreateIndex(
                name: "ix_tickets_creator_created",
                schema: "app",
                table: "tickets",
                columns: new[] { "created_by_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_tickets_status_priority_created",
                schema: "app",
                table: "tickets",
                columns: new[] { "status", "priority", "created_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_tickets_team_status",
                schema: "app",
                table: "tickets",
                columns: new[] { "assigned_team_id", "status" },
                filter: "assigned_team_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_tickets_reference",
                schema: "app",
                table: "tickets",
                column: "reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_users_email",
                schema: "app",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.Sql("""
                CREATE FUNCTION app.reject_history_mutation() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Historical rows are append-only.' USING ERRCODE = '55000';
                    RETURN NULL;
                END;
                $$;
                """);

            foreach (var table in new[] { "audit_logs", "ticket_comments", "ticket_predictions", "ai_messages" })
            {
                migrationBuilder.Sql($"CREATE TRIGGER protect_history BEFORE UPDATE OR DELETE OR TRUNCATE ON app.{table} FOR EACH STATEMENT EXECUTE FUNCTION app.reject_history_mutation();");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_messages",
                schema: "app");

            migrationBuilder.DropTable(
                name: "audit_logs",
                schema: "app");

            migrationBuilder.DropTable(
                name: "knowledge_documents",
                schema: "app");

            migrationBuilder.DropTable(
                name: "team_members",
                schema: "app");

            migrationBuilder.DropTable(
                name: "ticket_comments",
                schema: "app");

            migrationBuilder.DropTable(
                name: "ticket_predictions",
                schema: "app");

            migrationBuilder.DropTable(
                name: "ai_conversations",
                schema: "app");

            migrationBuilder.DropTable(
                name: "tickets",
                schema: "app");

            migrationBuilder.DropTable(
                name: "teams",
                schema: "app");

            migrationBuilder.DropTable(
                name: "users",
                schema: "app");

            migrationBuilder.Sql("DROP FUNCTION app.reject_history_mutation();");
        }
    }
}
