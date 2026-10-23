using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExperiments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "experiments",
                schema: "research",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    experiment_key = table.Column<string>(type: "character(64)", nullable: false),
                    knob_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    knob_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    baseline_value = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    candidate_value = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    baseline_run_key = table.Column<string>(type: "character(64)", nullable: false),
                    candidate_run_key = table.Column<string>(type: "character(64)", nullable: false),
                    from_date = table.Column<DateOnly>(type: "date", nullable: false),
                    to_date = table.Column<DateOnly>(type: "date", nullable: false),
                    hold_out_start = table.Column<DateOnly>(type: "date", nullable: true),
                    outcome = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    trials = table.Column<int>(type: "integer", nullable: false),
                    used_folds = table.Column<int>(type: "integer", nullable: false),
                    folds_better = table.Column<int>(type: "integer", nullable: false),
                    folds_needed = table.Column<int>(type: "integer", nullable: false),
                    required_improvement_r = table.Column<double>(type: "double precision", nullable: false),
                    baseline_trades = table.Column<int>(type: "integer", nullable: false),
                    candidate_trades = table.Column<int>(type: "integer", nullable: false),
                    pooled_baseline_mean_r = table.Column<double>(type: "double precision", nullable: true),
                    pooled_candidate_mean_r = table.Column<double>(type: "double precision", nullable: true),
                    baseline_total_r = table.Column<double>(type: "double precision", nullable: false),
                    candidate_total_r = table.Column<double>(type: "double precision", nullable: false),
                    expected_live_gap_r = table.Column<double>(type: "double precision", nullable: false),
                    compare_ms = table.Column<long>(type: "bigint", nullable: false),
                    reasons = table.Column<string>(type: "text", nullable: false),
                    report_text = table.Column<string>(type: "text", nullable: false),
                    parameters = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_experiments", x => x.id);
                    table.CheckConstraint("ck_experiments_shape", "experiment_key ~ '^[0-9a-f]{64}$' AND baseline_run_key ~ '^[0-9a-f]{64}$' AND candidate_run_key ~ '^[0-9a-f]{64}$' AND baseline_run_key <> candidate_run_key AND knob_kind IN ('Parameter', 'Exit', 'Slippage') AND outcome IN ('Better', 'NotBetter', 'Inconclusive', 'CheaperAssumption') AND trials >= 1 AND used_folds >= 0 AND folds_better >= 0 AND folds_better <= used_folds AND folds_needed >= 0 AND baseline_trades > 0 AND candidate_trades >= 0 AND candidate_trades <= baseline_trades AND compare_ms >= 0 AND (outcome <> 'Better' OR used_folds >= 6)");
                });

            migrationBuilder.CreateIndex(
                name: "ix_experiments_created_at",
                schema: "research",
                table: "experiments",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "uq_experiments_experiment_key",
                schema: "research",
                table: "experiments",
                column: "experiment_key",
                unique: true);

            // The runs must be stored runs (spec P6-11): the unique index on sim_runs (run_key) is the target.
            migrationBuilder.Sql("ALTER TABLE research.experiments ADD CONSTRAINT fk_experiments_sim_runs_baseline_run_key FOREIGN KEY (baseline_run_key) REFERENCES research.sim_runs (run_key) ON DELETE RESTRICT");
            migrationBuilder.Sql("ALTER TABLE research.experiments ADD CONSTRAINT fk_experiments_sim_runs_candidate_run_key FOREIGN KEY (candidate_run_key) REFERENCES research.sim_runs (run_key) ON DELETE RESTRICT");

            // Insert-only for the runtime role (spec P6-11, ADR 0072): an experiment is never changed or removed, so the 30-day count cannot be lowered.
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Research, "experiments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "experiments",
                schema: "research");
        }
    }
}
