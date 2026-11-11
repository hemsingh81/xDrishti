using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUniverseBasisAndScopeKnob : Migration
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
                sql: "source IN ('owner', 'grid', 'model', 'universe') AND knob_kind IN ('Parameter', 'Exit', 'Slippage', 'Scope') AND baseline_run_key ~ '^[0-9a-f]{64}$' AND (candidate_run_key IS NULL OR (candidate_run_key ~ '^[0-9a-f]{64}$' AND candidate_run_key <> baseline_run_key))");

            // BK-11b: the auto option of the tier proposer is one more key of the owner's universe settings (raw SQL: nothing maps this table in the EF model). No row is OFF.
            migrationBuilder.Sql("ALTER TABLE research.universe_settings_changes DROP CONSTRAINT ck_universe_settings_changes_key");
            migrationBuilder.Sql("ALTER TABLE research.universe_settings_changes ADD CONSTRAINT ck_universe_settings_changes_key CHECK (key IN ('learning.universe_min_trades_child', 'learning.universe_link_min_trades', 'learning.universe_registered_baskets', 'learning.universe_link.auto_propose'))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // the narrower check is added NOT VALID: rows of the new key that exist stay (history is never deleted), only new rows are checked; hypotheses of source universe or kind Scope are never deleted, so the hypotheses check below fails while one exists
            migrationBuilder.Sql("ALTER TABLE research.universe_settings_changes DROP CONSTRAINT ck_universe_settings_changes_key");
            migrationBuilder.Sql("ALTER TABLE research.universe_settings_changes ADD CONSTRAINT ck_universe_settings_changes_key CHECK (key IN ('learning.universe_min_trades_child', 'learning.universe_link_min_trades', 'learning.universe_registered_baskets')) NOT VALID");

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
    }
}
