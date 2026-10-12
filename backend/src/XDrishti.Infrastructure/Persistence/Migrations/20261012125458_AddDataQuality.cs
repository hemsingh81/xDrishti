using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDataQuality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "instrument_id",
                schema: "market",
                table: "data_jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "quality",
                schema: "market",
                table: "data_jobs",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{\"status\":\"notRequested\",\"startedAt\":null,\"finishedAt\":null,\"done\":0,\"total\":0,\"message\":null,\"code\":null,\"runId\":null,\"passed\":0,\"warned\":0,\"failed\":0}'::jsonb");

            migrationBuilder.CreateTable(
                name: "quality_runs",
                schema: "market",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trigger = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    status = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sessions = table.Column<int>(type: "integer", nullable: false),
                    instruments = table.Column<int>(type: "integer", nullable: false),
                    passed = table.Column<int>(type: "integer", nullable: false),
                    warned = table.Column<int>(type: "integer", nullable: false),
                    failed = table.Column<int>(type: "integer", nullable: false),
                    score = table.Column<double>(type: "double precision", nullable: true),
                    error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quality_runs", x => x.id);
                    table.CheckConstraint("ck_quality_runs_counts", "sessions >= 0 AND instruments >= 0 AND passed >= 0 AND warned >= 0 AND failed >= 0");
                    table.CheckConstraint("ck_quality_runs_status", "status IN ('running', 'succeeded', 'failed')");
                    table.CheckConstraint("ck_quality_runs_trigger", "trigger IN ('manual', 'daily', 'after_job')");
                    table.ForeignKey(
                        name: "fk_quality_runs_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalSchema: "market",
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "quality_results",
                schema: "market",
                columns: table => new
                {
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    score = table.Column<short>(type: "smallint", nullable: false),
                    bars_checked = table.Column<int>(type: "integer", nullable: false),
                    missing_minutes = table.Column<int>(type: "integer", nullable: false),
                    checks = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quality_results", x => new { x.run_id, x.instrument_id });
                    table.CheckConstraint("ck_quality_results_counts", "bars_checked >= 0 AND missing_minutes >= 0");
                    table.CheckConstraint("ck_quality_results_score", "score BETWEEN 0 AND 100");
                    table.CheckConstraint("ck_quality_results_status", "status IN ('pass', 'warning', 'fail')");
                    table.ForeignKey(
                        name: "fk_quality_results_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalSchema: "market",
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_quality_results_quality_runs_run_id",
                        column: x => x.run_id,
                        principalSchema: "market",
                        principalTable: "quality_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_quality_results_instrument_run",
                schema: "market",
                table: "quality_results",
                columns: new[] { "instrument_id", "run_id" });

            migrationBuilder.CreateIndex(
                name: "ix_quality_runs_instrument_id",
                schema: "market",
                table: "quality_runs",
                column: "instrument_id");

            migrationBuilder.CreateIndex(
                name: "ix_quality_runs_started_at",
                schema: "market",
                table: "quality_runs",
                column: "started_at");

            migrationBuilder.CreateIndex(
                name: "ux_quality_runs_one_running",
                schema: "market",
                table: "quality_runs",
                column: "status",
                unique: true,
                filter: "status = 'running'");

            // The runtime role records runs and results (insert) and moves a run along (update) but never deletes them: like the bars and the job records they are kept for good.
            foreach (var table in new[] { "quality_runs", "quality_results" })
            {
                migrationBuilder.GrantTableToAppRole(Schemas.Market, table);
                migrationBuilder.NeverDeleteForAppRole(Schemas.Market, table);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "quality_results",
                schema: "market");

            migrationBuilder.DropTable(
                name: "quality_runs",
                schema: "market");

            migrationBuilder.DropColumn(
                name: "instrument_id",
                schema: "market",
                table: "data_jobs");

            migrationBuilder.DropColumn(
                name: "quality",
                schema: "market",
                table: "data_jobs");
        }
    }
}
