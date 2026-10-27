using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The eval of the assistant (spec P9-09, ADR 0143): <c>ai.eval_runs</c>, <c>ai.eval_cases</c> and <c>ai.eval_baselines</c>, insert-only for the runtime role; <c>ai.eval_marker</c>, a one-row table that only the fixture seeder (the database owner) writes and that the runtime role cannot touch;
    /// and the reviewed view <c>aiv.eval_marker</c> through which the eval tool endpoint proves that it reads the fixture database. Empty in every database but the fixture's.
    /// </summary>
    public partial class AddEvalRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "eval_marker",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<short>(type: "smallint", nullable: false),
                    fixture_hash = table.Column<string>(type: "character(64)", nullable: false),
                    seeded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_eval_marker", x => x.id);
                    table.CheckConstraint("ck_ai_eval_marker_single", "id = 1");
                });

            migrationBuilder.CreateTable(
                name: "eval_runs",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    target = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    profile = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    sets = table.Column<string[]>(type: "text[]", nullable: false),
                    repeats = table.Column<int>(type: "integer", nullable: false),
                    gating = table.Column<bool>(type: "boolean", nullable: false),
                    model_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    model_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    runtime_digest = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    params_json = table.Column<string>(type: "jsonb", nullable: false),
                    prompt_versions_json = table.Column<string>(type: "jsonb", nullable: false),
                    sets_hash = table.Column<string>(type: "character(64)", nullable: false),
                    thresholds_hash = table.Column<string>(type: "character(64)", nullable: false),
                    fixture_hash = table.Column<string>(type: "character(64)", nullable: false),
                    fingerprint = table.Column<string>(type: "character(64)", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    verdict = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    summary_json = table.Column<string>(type: "jsonb", nullable: false),
                    actor = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_eval_runs", x => x.id);
                    table.CheckConstraint("ck_ai_eval_runs_gating", "NOT gating OR (repeats >= 2 AND target = 'builtin' AND status = 'completed')");
                    table.CheckConstraint("ck_ai_eval_runs_repeats", "repeats BETWEEN 1 AND 5");
                    table.CheckConstraint("ck_ai_eval_runs_time", "finished_at >= started_at");
                    table.CheckConstraint("ck_ai_eval_runs_verdict", "verdict IN ('PASS', 'FAIL', 'INCOMPLETE')");
                });

            migrationBuilder.CreateTable(
                name: "eval_baselines",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    accepted_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_eval_baselines", x => x.id);
                    table.CheckConstraint("ck_ai_eval_baselines_actor", "accepted_by = 'cli:xd'");
                    table.ForeignKey(
                        name: "fk_eval_baselines_eval_runs_run_id",
                        column: x => x.run_id,
                        principalSchema: "ai",
                        principalTable: "eval_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "eval_cases",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    set = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    case_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    repeat = table.Column<int>(type: "integer", nullable: false),
                    passed = table.Column<bool>(type: "boolean", nullable: false),
                    grounded = table.Column<bool>(type: "boolean", nullable: true),
                    score = table.Column<decimal>(type: "numeric(6,4)", nullable: true),
                    detail_json = table.Column<string>(type: "jsonb", nullable: false),
                    answer = table.Column<string>(type: "text", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tool_calls = table.Column<int>(type: "integer", nullable: false),
                    latency_ms = table.Column<int>(type: "integer", nullable: false),
                    prompt_tokens = table.Column<int>(type: "integer", nullable: true),
                    completion_tokens = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_eval_cases", x => x.id);
                    table.CheckConstraint("ck_ai_eval_cases_counts", "tool_calls >= 0 AND latency_ms >= 0");
                    table.CheckConstraint("ck_ai_eval_cases_repeat", "repeat >= 1");
                    table.CheckConstraint("ck_ai_eval_cases_set", "\"set\" IN ('data', 'plans', 'ideas', 'events')");
                    table.ForeignKey(
                        name: "fk_eval_cases_eval_runs_run_id",
                        column: x => x.run_id,
                        principalSchema: "ai",
                        principalTable: "eval_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_ai_eval_baselines_run_id",
                schema: "ai",
                table: "eval_baselines",
                column: "run_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_ai_eval_cases_run_set_case_repeat",
                schema: "ai",
                table: "eval_cases",
                columns: new[] { "run_id", "set", "case_id", "repeat" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ai_eval_runs_finished_at",
                schema: "ai",
                table: "eval_runs",
                column: "finished_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_ai_eval_runs_gating",
                schema: "ai",
                table: "eval_runs",
                column: "gating");

            // Insert-only for the runtime role (spec P9-09 AC 14): the default privileges give it DML on new tables; take UPDATE and DELETE away.
            migrationBuilder.MakeAppendOnlyForAppRole("ai", "eval_runs");
            migrationBuilder.MakeAppendOnlyForAppRole("ai", "eval_cases");
            migrationBuilder.MakeAppendOnlyForAppRole("ai", "eval_baselines");

            // The marker: no right at all for the runtime role (AC 13); the seeder writes it as the owner and the tool role reads it through the view.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_app') THEN
                        REVOKE ALL ON ai.eval_marker FROM xd_app;
                    END IF;
                END $$
                """);
            migrationBuilder.Sql("REVOKE ALL ON ai.eval_marker FROM PUBLIC");

            // The reviewed view of the marker (P9-02 AC 13a: the two revokes every view of aiv ends with, then the grant to the two read-only roles).
            migrationBuilder.Sql("CREATE VIEW aiv.eval_marker AS SELECT t.fixture_hash AS fixture_hash, t.seeded_at AS seeded_at FROM ai.eval_marker t");
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_app') THEN
                        REVOKE ALL ON aiv.eval_marker FROM xd_app;
                    END IF;
                END $$
                """);
            migrationBuilder.Sql("REVOKE ALL ON aiv.eval_marker FROM PUBLIC");
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_mcp_read') THEN
                        GRANT SELECT ON aiv.eval_marker TO xd_mcp_read, xd_mcp_sql;
                    END IF;
                END $$
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW aiv.eval_marker");

            migrationBuilder.DropTable(
                name: "eval_baselines",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "eval_cases",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "eval_marker",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "eval_runs",
                schema: "ai");
        }
    }
}
