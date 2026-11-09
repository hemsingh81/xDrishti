using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Spec AU-11, ADR 0243: <c>research.sim_runs.purpose</c> (<c>research</c> default, so every existing run reads as research and no report changes; <c>training</c> for bulk simulation), a partial index for training runs, the ledger kind
    /// <c>training_sweep</c>, and the three AI read views now show research runs only. Down refuses while a training run exists (dropping the column would turn it into evidence) and removes the training_sweep ledger rows the old check refuses.
    /// </summary>
    public partial class AddSimRunPurpose : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_test_ledger_trial",
                schema: "lab",
                table: "test_ledger");

            migrationBuilder.AddColumn<string>(
                name: "purpose",
                schema: "research",
                table: "sim_runs",
                type: "text",
                nullable: false,
                defaultValue: "research");

            migrationBuilder.AddCheckConstraint(
                name: "ck_test_ledger_trial",
                schema: "lab",
                table: "test_ledger",
                sql: "trial_kind IN ('run','replan','null','training_sweep') AND extra_trials >= 0");

            migrationBuilder.CreateIndex(
                name: "ix_sim_runs_training",
                schema: "research",
                table: "sim_runs",
                column: "created_at",
                filter: "purpose = 'training'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_sim_runs_purpose",
                schema: "research",
                table: "sim_runs",
                sql: "purpose IN ('research', 'training')");

            // the report engine reads the stored runs as the read role through the column-level grant of P9-05; its fence on purpose needs the column
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_mcp_read') THEN
                        GRANT SELECT (purpose) ON research.sim_runs TO xd_mcp_read;
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql("""
                CREATE OR REPLACE VIEW aiv.sim_runs AS
                    SELECT
                        t.run_key AS run_key,
                        t.engine_version AS engine_version,
                        t.strategy_id AS strategy_id,
                        t.definition_sha256 AS definition_sha256,
                        t.from_date AS from_date,
                        t.to_date AS to_date,
                        t.universe_kind AS universe_kind,
                        t.universe_label AS universe_label,
                        t.instruments AS instruments,
                        t.instruments_run AS instruments_run,
                        t.trades_in_window AS trades_in_window,
                        t.candidates_in_window AS candidates_in_window,
                        t.trade_count AS trade_count,
                        t.open_at_end_count AS open_at_end_count,
                        t.fills AS fills,
                        t.costs_sheet_id AS costs_sheet_id,
                        t.costs_rates_sha256 AS costs_rates_sha256,
                        t.created_at AS created_at
                    FROM research.sim_runs t
                    WHERE t.purpose = 'research'
                """);

            migrationBuilder.Sql("""
                CREATE OR REPLACE VIEW aiv.sim_trades AS
                    SELECT
                        r.run_key AS run_key,
                        t.ordinal AS ordinal,
                        t.trade_date AS trade_date,
                        t.symbol AS symbol,
                        t.strategy_id AS strategy_id,
                        t.side AS side,
                        t.trigger_timeframe AS trigger_timeframe,
                        to_timestamp(t.entry_epoch_seconds) AS entry_at,
                        to_timestamp(t.exit_epoch_seconds) AS exit_at,
                        t.entry_price AS entry_price,
                        t.exit_price AS exit_price,
                        t.entry_filled_price AS entry_filled_price,
                        t.exit_filled_price AS exit_filled_price,
                        t.stop_price AS stop_price,
                        t.target_price AS target_price,
                        t.exit_reason AS exit_reason,
                        t.r_exact AS r_exact,
                        t.r_filled AS r_filled,
                        t.r_net AS r_net,
                        t.mae_r AS mae_r,
                        t.mfe_r AS mfe_r,
                        t.slippage_r AS slippage_r,
                        t.cost_r AS cost_r,
                        t.cost_total AS cost_total,
                        t.cost_quantity AS cost_quantity,
                        t.sessions_held AS sessions_held,
                        t.fills AS fills,
                        t.costs AS costs,
                        t.open_at_end AS open_at_end,
                        t.instrument_id AS instrument_id
                    FROM research.sim_trades t JOIN research.sim_runs r ON r.id = t.run_id
                    WHERE r.purpose = 'research'
                """);

            migrationBuilder.Sql("""
                CREATE OR REPLACE VIEW aiv.sim_trade_legs AS
                    SELECT
                        r.run_key AS run_key,
                        t.ordinal AS trade_ordinal,
                        t.leg_ordinal AS leg_ordinal,
                        to_timestamp(t.epoch_seconds) AS leg_at,
                        t.price AS price,
                        t.filled_price AS filled_price,
                        t.share AS share,
                        t.kind AS kind,
                        t.reason AS reason
                    FROM research.sim_trade_legs t JOIN research.sim_runs r ON r.id = t.run_id
                    WHERE r.purpose = 'research'
                """);

            migrationBuilder.Sql("""
                CREATE OR REPLACE VIEW aiv.test_ledger AS
                    SELECT
                        t.strategy_id AS strategy_id,
                        t.version AS version,
                        t.strategy_hash AS strategy_hash,
                        t.parent_hash AS parent_hash,
                        r.key AS lab_run_key,
                        t.run_key AS run_key,
                        t.universe_label AS universe_label,
                        t.window_from AS window_from,
                        t.window_to AS window_to,
                        t.method AS method,
                        t.created_at AS created_at
                    FROM lab.test_ledger t LEFT JOIN lab.lab_runs r ON r.id = t.lab_run_id
                    WHERE t.trial_kind <> 'training_sweep'
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // undo the column grant
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_mcp_read') THEN
                        REVOKE SELECT (purpose) ON research.sim_runs FROM xd_mcp_read;
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM research.sim_runs WHERE purpose = 'training') THEN
                        RAISE EXCEPTION 'Training runs exist; removing the purpose column would turn them into evidence. Delete them first.';
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql("DELETE FROM lab.test_ledger WHERE trial_kind = 'training_sweep';");


            migrationBuilder.Sql("""
                CREATE OR REPLACE VIEW aiv.test_ledger AS
                    SELECT
                        t.strategy_id AS strategy_id,
                        t.version AS version,
                        t.strategy_hash AS strategy_hash,
                        t.parent_hash AS parent_hash,
                        r.key AS lab_run_key,
                        t.run_key AS run_key,
                        t.universe_label AS universe_label,
                        t.window_from AS window_from,
                        t.window_to AS window_to,
                        t.method AS method,
                        t.created_at AS created_at
                    FROM lab.test_ledger t LEFT JOIN lab.lab_runs r ON r.id = t.lab_run_id
                """);

            migrationBuilder.Sql("""
                CREATE OR REPLACE VIEW aiv.sim_runs AS
                    SELECT
                        t.run_key AS run_key,
                        t.engine_version AS engine_version,
                        t.strategy_id AS strategy_id,
                        t.definition_sha256 AS definition_sha256,
                        t.from_date AS from_date,
                        t.to_date AS to_date,
                        t.universe_kind AS universe_kind,
                        t.universe_label AS universe_label,
                        t.instruments AS instruments,
                        t.instruments_run AS instruments_run,
                        t.trades_in_window AS trades_in_window,
                        t.candidates_in_window AS candidates_in_window,
                        t.trade_count AS trade_count,
                        t.open_at_end_count AS open_at_end_count,
                        t.fills AS fills,
                        t.costs_sheet_id AS costs_sheet_id,
                        t.costs_rates_sha256 AS costs_rates_sha256,
                        t.created_at AS created_at
                    FROM research.sim_runs t
                """);

            migrationBuilder.Sql("""
                CREATE OR REPLACE VIEW aiv.sim_trades AS
                    SELECT
                        r.run_key AS run_key,
                        t.ordinal AS ordinal,
                        t.trade_date AS trade_date,
                        t.symbol AS symbol,
                        t.strategy_id AS strategy_id,
                        t.side AS side,
                        t.trigger_timeframe AS trigger_timeframe,
                        to_timestamp(t.entry_epoch_seconds) AS entry_at,
                        to_timestamp(t.exit_epoch_seconds) AS exit_at,
                        t.entry_price AS entry_price,
                        t.exit_price AS exit_price,
                        t.entry_filled_price AS entry_filled_price,
                        t.exit_filled_price AS exit_filled_price,
                        t.stop_price AS stop_price,
                        t.target_price AS target_price,
                        t.exit_reason AS exit_reason,
                        t.r_exact AS r_exact,
                        t.r_filled AS r_filled,
                        t.r_net AS r_net,
                        t.mae_r AS mae_r,
                        t.mfe_r AS mfe_r,
                        t.slippage_r AS slippage_r,
                        t.cost_r AS cost_r,
                        t.cost_total AS cost_total,
                        t.cost_quantity AS cost_quantity,
                        t.sessions_held AS sessions_held,
                        t.fills AS fills,
                        t.costs AS costs,
                        t.open_at_end AS open_at_end,
                        t.instrument_id AS instrument_id
                    FROM research.sim_trades t JOIN research.sim_runs r ON r.id = t.run_id
                """);

            migrationBuilder.Sql("""
                CREATE OR REPLACE VIEW aiv.sim_trade_legs AS
                    SELECT
                        r.run_key AS run_key,
                        t.ordinal AS trade_ordinal,
                        t.leg_ordinal AS leg_ordinal,
                        to_timestamp(t.epoch_seconds) AS leg_at,
                        t.price AS price,
                        t.filled_price AS filled_price,
                        t.share AS share,
                        t.kind AS kind,
                        t.reason AS reason
                    FROM research.sim_trade_legs t JOIN research.sim_runs r ON r.id = t.run_id
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_test_ledger_trial",
                schema: "lab",
                table: "test_ledger");

            migrationBuilder.DropIndex(
                name: "ix_sim_runs_training",
                schema: "research",
                table: "sim_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_sim_runs_purpose",
                schema: "research",
                table: "sim_runs");

            migrationBuilder.DropColumn(
                name: "purpose",
                schema: "research",
                table: "sim_runs");

            migrationBuilder.AddCheckConstraint(
                name: "ck_test_ledger_trial",
                schema: "lab",
                table: "test_ledger",
                sql: "trial_kind IN ('run','replan','null') AND extra_trials >= 0");
        }
    }
}
