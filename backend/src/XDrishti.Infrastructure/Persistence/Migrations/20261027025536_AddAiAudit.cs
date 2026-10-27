using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    public partial class AddAiAudit : Migration
    {
        /// <summary>
        /// The audit of the assistant (spec P9-03, ADR 0138): conversations, messages, tool calls, prompts and the model calls outside chat. Every table is insert-only for the runtime role: the schema is re-granted first (that gives UPDATE and DELETE on every table of the schema, <c>ai.settings</c>
        /// included, and the view <c>ai.account_labels</c>, which is taken away again) and then UPDATE and DELETE are taken away again from all six. The two read-only roles of the tool server get nothing here (they read <c>aiv</c> and, for the switch, <c>ai.settings</c> only). The identity ids of messages and calls are drawn by the database.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "conversations",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    origin = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    settings_id = table.Column<long>(type: "bigint", nullable: true),
                    model_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    llm_build = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversations", x => x.id);
                    table.CheckConstraint("ck_ai_conversations_origin", "origin IN ('chat', 'eval', 'job')");
                    table.ForeignKey(
                        name: "fk_conversations_settings_settings_id",
                        column: x => x.settings_id,
                        principalSchema: "ai",
                        principalTable: "settings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "model_calls",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    purpose = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    caller = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    prompt_hash = table.Column<string>(type: "character(64)", nullable: false),
                    model_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    model_sha256 = table.Column<string>(type: "character(64)", nullable: true),
                    prompt_tokens = table.Column<int>(type: "integer", nullable: true),
                    completion_tokens = table.Column<int>(type: "integer", nullable: true),
                    total_ms = table.Column<int>(type: "integer", nullable: false),
                    finish_reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    error_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_model_calls", x => x.id);
                    table.CheckConstraint("ck_ai_model_calls_purpose", "purpose IN ('briefing', 'draft', 'proposal', 'eval', 'other')");
                    table.CheckConstraint("ck_ai_model_calls_status", "status IN ('ok', 'error', 'timeout', 'cancelled')");
                });

            migrationBuilder.CreateTable(
                name: "prompts",
                schema: "ai",
                columns: table => new
                {
                    hash = table.Column<string>(type: "character(64)", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_prompts", x => x.hash);
                });

            migrationBuilder.CreateTable(
                name: "tool_calls",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    turn_id = table.Column<Guid>(type: "uuid", nullable: false),
                    call_index = table.Column<int>(type: "integer", nullable: false),
                    tool_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    arguments = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    error_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    result = table.Column<string>(type: "jsonb", nullable: true),
                    result_bytes = table.Column<int>(type: "integer", nullable: false),
                    rows = table.Column<int>(type: "integer", nullable: false),
                    truncated = table.Column<bool>(type: "boolean", nullable: false),
                    evidence = table.Column<string>(type: "jsonb", nullable: true),
                    redactions = table.Column<int>(type: "integer", nullable: false),
                    injection_flags = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    duration_ms = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tool_calls", x => x.id);
                    table.CheckConstraint("ck_ai_tool_calls_result_bytes", "result_bytes >= 0 AND result_bytes <= 10485760");
                    table.CheckConstraint("ck_ai_tool_calls_status", "status IN ('ok', 'error', 'timeout', 'refused', 'disabled')");
                    table.ForeignKey(
                        name: "fk_tool_calls_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalSchema: "ai",
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    turn_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seq = table.Column<int>(type: "integer", nullable: false),
                    role = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    prompt_hash = table.Column<string>(type: "character(64)", nullable: true),
                    model_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    prompt_tokens = table.Column<int>(type: "integer", nullable: true),
                    completion_tokens = table.Column<int>(type: "integer", nullable: true),
                    first_token_ms = table.Column<int>(type: "integer", nullable: true),
                    total_ms = table.Column<int>(type: "integer", nullable: true),
                    finish_reason = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    grounding_status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    ungrounded = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_messages", x => x.id);
                    table.CheckConstraint("ck_ai_messages_finish_reason", "finish_reason IS NULL OR finish_reason IN ('stop', 'length', 'tool_limit', 'cancelled', 'error')");
                    table.CheckConstraint("ck_ai_messages_grounding_status", "grounding_status IS NULL OR grounding_status IN ('grounded', 'ungrounded', 'no_numbers', 'not_checked')");
                    table.CheckConstraint("ck_ai_messages_role", "role IN ('user', 'assistant')");
                    table.ForeignKey(
                        name: "fk_messages_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalSchema: "ai",
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_messages_prompts_prompt_hash",
                        column: x => x.prompt_hash,
                        principalSchema: "ai",
                        principalTable: "prompts",
                        principalColumn: "hash",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ai_conversations_created_at",
                schema: "ai",
                table: "conversations",
                column: "created_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_conversations_settings_id",
                schema: "ai",
                table: "conversations",
                column: "settings_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_messages_turn_id",
                schema: "ai",
                table: "messages",
                column: "turn_id");

            migrationBuilder.CreateIndex(
                name: "ix_messages_prompt_hash",
                schema: "ai",
                table: "messages",
                column: "prompt_hash");

            migrationBuilder.CreateIndex(
                name: "ux_ai_messages_conversation_id_seq",
                schema: "ai",
                table: "messages",
                columns: new[] { "conversation_id", "seq" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_ai_tool_calls_conversation_id_turn_id_call_index",
                schema: "ai",
                table: "tool_calls",
                columns: new[] { "conversation_id", "turn_id", "call_index" },
                unique: true);

            migrationBuilder.GrantSchemaUsageToAppRole("ai");
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_app') THEN
                        REVOKE ALL ON ai.account_labels FROM xd_app;
                    END IF;
                END $$
                """);
            foreach (var table in new[] { "settings", "prompts", "conversations", "messages", "tool_calls", "model_calls" })
            {
                migrationBuilder.MakeAppendOnlyForAppRole("ai", table);
            }
        }

        /// <summary>Drops the five tables this migration made. <c>ai.settings</c> belongs to the migration before and is not touched; the schema grants stay as they were (the earlier migration's Down removes the schema).</summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "messages",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "model_calls",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "tool_calls",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "prompts",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "conversations",
                schema: "ai");
        }
    }
}
