using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStrategyDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "strategy_drafts",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    idea = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    fail_reason = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    via = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    basket_id = table.Column<Guid>(type: "uuid", nullable: true),
                    yaml = table.Column<string>(type: "text", nullable: true),
                    answer_json = table.Column<string>(type: "jsonb", nullable: false),
                    attempts_json = table.Column<string>(type: "jsonb", nullable: false),
                    problems_json = table.Column<string>(type: "jsonb", nullable: false),
                    findings_json = table.Column<string>(type: "jsonb", nullable: false),
                    model_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    model_sha256 = table.Column<string>(type: "character(64)", nullable: true),
                    prompt_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    sheet_hash = table.Column<string>(type: "character(64)", nullable: false),
                    params_json = table.Column<string>(type: "jsonb", nullable: false),
                    requested_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_strategy_drafts", x => x.id);
                    table.CheckConstraint("ck_ai_strategy_drafts_status", "status IN ('valid', 'invalid', 'declined', 'failed')");
                    table.CheckConstraint("ck_ai_strategy_drafts_via", "via IN ('job', 'chat_tool')");
                    table.CheckConstraint("ck_ai_strategy_drafts_yaml", "status <> 'valid' OR yaml IS NOT NULL");
                });

            migrationBuilder.CreateTable(
                name: "strategy_draft_decisions",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    draft_id = table.Column<long>(type: "bigint", nullable: false),
                    decision = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    strategy_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    strategy_version = table.Column<int>(type: "integer", nullable: true),
                    lab_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    edited = table.Column<bool>(type: "boolean", nullable: false),
                    auto = table.Column<bool>(type: "boolean", nullable: false),
                    settings_id = table.Column<long>(type: "bigint", nullable: true),
                    snapshot_hash = table.Column<string>(type: "character(64)", nullable: true),
                    actor = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_strategy_draft_decisions", x => x.id);
                    table.CheckConstraint("ck_ai_strategy_draft_decisions_auto", "NOT auto OR (decision = 'confirmed' AND settings_id IS NOT NULL AND snapshot_hash IS NOT NULL AND actor = 'system:auto-draft')");
                    table.CheckConstraint("ck_ai_strategy_draft_decisions_confirmed", "decision <> 'confirmed' OR (strategy_id IS NOT NULL AND strategy_version IS NOT NULL)");
                    table.CheckConstraint("ck_ai_strategy_draft_decisions_decision", "decision IN ('confirmed', 'discarded')");
                    table.ForeignKey(
                        name: "fk_strategy_draft_decisions_strategy_drafts_draft_id",
                        column: x => x.draft_id,
                        principalSchema: "ai",
                        principalTable: "strategy_drafts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ai_strategy_draft_decisions_decided_at",
                schema: "ai",
                table: "strategy_draft_decisions",
                column: "decided_at");

            migrationBuilder.CreateIndex(
                name: "ux_ai_strategy_draft_decisions_draft_id",
                schema: "ai",
                table: "strategy_draft_decisions",
                column: "draft_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ai_strategy_drafts_created_at",
                schema: "ai",
                table: "strategy_drafts",
                column: "created_at",
                descending: new bool[0]);

            // Insert-only for the runtime role (spec P9-07 AC 11): the default privileges give it DML on new tables; take UPDATE and DELETE away.
            migrationBuilder.MakeAppendOnlyForAppRole("ai", "strategy_drafts");
            migrationBuilder.MakeAppendOnlyForAppRole("ai", "strategy_draft_decisions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "strategy_draft_decisions",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "strategy_drafts",
                schema: "ai");
        }
    }
}
