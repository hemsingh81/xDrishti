using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLabRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "lab_runs",
                schema: "lab",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character(64)", nullable: false),
                    strategy_id = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    strategy_hash = table.Column<string>(type: "character(64)", nullable: false),
                    universe = table.Column<string>(type: "jsonb", nullable: false),
                    window_from = table.Column<DateOnly>(type: "date", nullable: false),
                    window_to = table.Column<DateOnly>(type: "date", nullable: false),
                    method = table.Column<string>(type: "text", nullable: false),
                    config_hash = table.Column<string>(type: "character(64)", nullable: false),
                    engine_version = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    stage = table.Column<string>(type: "text", nullable: true),
                    progress_done = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    progress_total = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    sim_run_keys = table.Column<string[]>(type: "text[]", nullable: false),
                    counts = table.Column<string>(type: "jsonb", nullable: true),
                    problems = table.Column<string>(type: "jsonb", nullable: false),
                    stages = table.Column<string>(type: "jsonb", nullable: false),
                    labels = table.Column<string>(type: "jsonb", nullable: false),
                    error = table.Column<string>(type: "text", nullable: true),
                    host = table.Column<string>(type: "text", nullable: true),
                    cancel_requested = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lab_runs", x => x.id);
                    table.CheckConstraint("ck_lab_runs_key", "key ~ '^[0-9a-f]{64}$' AND strategy_hash ~ '^[0-9a-f]{64}$' AND config_hash ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_lab_runs_progress", "progress_done >= 0 AND progress_total >= 0");
                    table.CheckConstraint("ck_lab_runs_state", "state IN ('queued', 'running', 'succeeded', 'failed', 'cancelled', 'interrupted')");
                    table.CheckConstraint("ck_lab_runs_window", "window_from <= window_to");
                    table.ForeignKey(
                        name: "fk_lab_runs_strategy_version",
                        column: x => x.strategy_hash,
                        principalSchema: "lab",
                        principalTable: "strategy_versions",
                        principalColumn: "hash",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_lab_runs_queued",
                schema: "lab",
                table: "lab_runs",
                column: "created_at",
                filter: "state = 'queued'");

            migrationBuilder.CreateIndex(
                name: "ix_lab_runs_strategy_created",
                schema: "lab",
                table: "lab_runs",
                columns: new[] { "strategy_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_lab_runs_strategy_hash",
                schema: "lab",
                table: "lab_runs",
                column: "strategy_hash");

            migrationBuilder.CreateIndex(
                name: "uq_lab_runs_active_key",
                schema: "lab",
                table: "lab_runs",
                column: "key",
                unique: true,
                filter: "state IN ('queued', 'running', 'succeeded')");

            // The runtime role inserts a run and updates only its state columns; it never deletes (spec P5-02, ADR 0058). Not GrantSchemaUsageToAppRole: that would re-grant UPDATE and DELETE on the append-only
            // tables of the schema (strategies, strategy_versions); default privileges give the new table plain DML, which is cut back here.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_app') THEN
                        REVOKE UPDATE, DELETE ON lab.lab_runs FROM xd_app;
                        GRANT SELECT, INSERT ON lab.lab_runs TO xd_app;
                        GRANT UPDATE (state, stage, progress_done, progress_total, sim_run_keys, counts, problems, stages, error, host, cancel_requested, started_at, finished_at, updated_at) ON lab.lab_runs TO xd_app;
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lab_runs",
                schema: "lab");
        }
    }
}
