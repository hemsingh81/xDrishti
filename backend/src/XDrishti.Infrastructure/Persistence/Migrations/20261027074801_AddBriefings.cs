using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBriefings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "briefings",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    kind = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    subject = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    source = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    reason = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    text = table.Column<string>(type: "text", nullable: false),
                    deterministic_text = table.Column<string>(type: "text", nullable: false),
                    facts_json = table.Column<string>(type: "jsonb", nullable: false),
                    facts_hash = table.Column<string>(type: "character(64)", nullable: false),
                    model_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    model_sha256 = table.Column<string>(type: "character(64)", nullable: true),
                    prompt_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    params_json = table.Column<string>(type: "jsonb", nullable: false),
                    word_count = table.Column<int>(type: "integer", nullable: false),
                    built_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_briefings", x => x.id);
                    table.CheckConstraint("ck_ai_briefings_kind", "kind IN ('daily', 'weekly')");
                    table.CheckConstraint("ck_ai_briefings_model_source", "source <> 'model' OR model_id IS NOT NULL");
                    table.CheckConstraint("ck_ai_briefings_reason", "reason IS NULL OR reason IN ('model_off', 'model_unavailable', 'guard_rejected', 'facts_thin')");
                    table.CheckConstraint("ck_ai_briefings_reason_iff_deterministic", "(source = 'model') = (reason IS NULL)");
                    table.CheckConstraint("ck_ai_briefings_source", "source IN ('model', 'deterministic')");
                    table.CheckConstraint("ck_ai_briefings_version", "version >= 1");
                    table.CheckConstraint("ck_ai_briefings_weekly_footer", "kind <> 'weekly' OR text LIKE '%Nothing was applied%'");
                });

            migrationBuilder.CreateTable(
                name: "briefing_attempts",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    briefing_id = table.Column<long>(type: "bigint", nullable: false),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    model_call_id = table.Column<long>(type: "bigint", nullable: true),
                    text = table.Column<string>(type: "text", nullable: false),
                    passed = table.Column<bool>(type: "boolean", nullable: false),
                    findings_json = table.Column<string>(type: "jsonb", nullable: false),
                    latency_ms = table.Column<int>(type: "integer", nullable: false),
                    prompt_tokens = table.Column<int>(type: "integer", nullable: true),
                    completion_tokens = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_briefing_attempts", x => x.id);
                    table.CheckConstraint("ck_ai_briefing_attempts_attempt", "attempt >= 1");
                    table.ForeignKey(
                        name: "fk_briefing_attempts_briefings_briefing_id",
                        column: x => x.briefing_id,
                        principalSchema: "ai",
                        principalTable: "briefings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_briefing_attempts_model_calls_model_call_id",
                        column: x => x.model_call_id,
                        principalSchema: "ai",
                        principalTable: "model_calls",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_briefing_attempts_model_call_id",
                schema: "ai",
                table: "briefing_attempts",
                column: "model_call_id");

            migrationBuilder.CreateIndex(
                name: "ux_ai_briefing_attempts_briefing_id_attempt",
                schema: "ai",
                table: "briefing_attempts",
                columns: new[] { "briefing_id", "attempt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ai_briefings_created_at",
                schema: "ai",
                table: "briefings",
                column: "created_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ux_ai_briefings_kind_subject_version",
                schema: "ai",
                table: "briefings",
                columns: new[] { "kind", "subject", "version" },
                unique: true);

            // Insert-only for the runtime role (spec P9-06 AC 10): the default privileges give it DML on new tables; take UPDATE and DELETE away.
            migrationBuilder.MakeAppendOnlyForAppRole("ai", "briefings");
            migrationBuilder.MakeAppendOnlyForAppRole("ai", "briefing_attempts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "briefing_attempts",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "briefings",
                schema: "ai");
        }
    }
}
