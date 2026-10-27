using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowModelHypothesisSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_hypotheses_shape",
                schema: "research",
                table: "hypotheses");

            migrationBuilder.AddCheckConstraint(
                name: "ck_hypotheses_shape",
                schema: "research",
                table: "hypotheses",
                sql: "source IN ('owner', 'grid', 'model') AND knob_kind IN ('Parameter', 'Exit', 'Slippage') AND baseline_run_key ~ '^[0-9a-f]{64}$' AND (candidate_run_key IS NULL OR (candidate_run_key ~ '^[0-9a-f]{64}$' AND candidate_run_key <> baseline_run_key))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_hypotheses_shape",
                schema: "research",
                table: "hypotheses");

            migrationBuilder.AddCheckConstraint(
                name: "ck_hypotheses_shape",
                schema: "research",
                table: "hypotheses",
                sql: "source IN ('owner', 'grid') AND knob_kind IN ('Parameter', 'Exit', 'Slippage') AND baseline_run_key ~ '^[0-9a-f]{64}$' AND (candidate_run_key IS NULL OR (candidate_run_key ~ '^[0-9a-f]{64}$' AND candidate_run_key <> baseline_run_key))");
        }
    }
}
