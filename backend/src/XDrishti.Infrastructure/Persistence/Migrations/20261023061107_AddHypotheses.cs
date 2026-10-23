using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHypotheses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hypotheses",
                schema: "research",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    hypothesis_id = table.Column<long>(type: "bigint", nullable: true),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    from_state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    source = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    cell_key = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    knob_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    knob_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    baseline_value = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    candidate_value = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    rationale = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    baseline_run_key = table.Column<string>(type: "character(64)", nullable: false),
                    candidate_run_key = table.Column<string>(type: "character(64)", nullable: true),
                    experiment_id = table.Column<long>(type: "bigint", nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    actor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hypotheses", x => x.id);
                    table.CheckConstraint("ck_hypotheses_links", "(state NOT IN ('running', 'judged', 'accepted') OR candidate_run_key IS NOT NULL) AND (state NOT IN ('judged', 'accepted') OR experiment_id IS NOT NULL)");
                    table.CheckConstraint("ck_hypotheses_shape", "source IN ('owner', 'grid') AND knob_kind IN ('Parameter', 'Exit', 'Slippage') AND baseline_run_key ~ '^[0-9a-f]{64}$' AND (candidate_run_key IS NULL OR (candidate_run_key ~ '^[0-9a-f]{64}$' AND candidate_run_key <> baseline_run_key))");
                    table.CheckConstraint("ck_hypotheses_state", "state IN ('proposed', 'approved', 'running', 'judged', 'accepted', 'rejected', 'expired') AND (from_state IS NULL OR from_state IN ('proposed', 'approved', 'running', 'judged', 'accepted', 'rejected', 'expired')) AND (state = 'proposed') = (from_state IS NULL) AND (state = 'proposed') = (hypothesis_id IS NULL) AND state IS DISTINCT FROM from_state");
                    table.ForeignKey(
                        name: "fk_hypotheses_hypotheses_hypothesis_id",
                        column: x => x.hypothesis_id,
                        principalSchema: "research",
                        principalTable: "hypotheses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_hypotheses_experiment_id",
                schema: "research",
                table: "hypotheses",
                column: "experiment_id");

            migrationBuilder.CreateIndex(
                name: "ix_hypotheses_hypothesis_id_id",
                schema: "research",
                table: "hypotheses",
                columns: new[] { "hypothesis_id", "id" });

            // The runs must be stored runs and the experiment a recorded one (spec P6-10).
            migrationBuilder.Sql("ALTER TABLE research.hypotheses ADD CONSTRAINT fk_hypotheses_sim_runs_baseline_run_key FOREIGN KEY (baseline_run_key) REFERENCES research.sim_runs (run_key) ON DELETE RESTRICT");
            migrationBuilder.Sql("ALTER TABLE research.hypotheses ADD CONSTRAINT fk_hypotheses_sim_runs_candidate_run_key FOREIGN KEY (candidate_run_key) REFERENCES research.sim_runs (run_key) ON DELETE RESTRICT");
            migrationBuilder.Sql("ALTER TABLE research.hypotheses ADD CONSTRAINT fk_hypotheses_experiments_experiment_id FOREIGN KEY (experiment_id) REFERENCES research.experiments (id) ON DELETE RESTRICT");

            // Insert-only for the runtime role (spec P6-10, ADR 0076): a state change is a new row.
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Research, "hypotheses");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hypotheses",
                schema: "research");
        }
    }
}
