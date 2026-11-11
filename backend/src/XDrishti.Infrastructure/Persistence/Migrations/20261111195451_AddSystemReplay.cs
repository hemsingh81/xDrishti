using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The system replay (spec SR-01 part B, ADR 0272): <c>research.replay_runs</c>, <c>research.replay_sessions</c> and <c>research.replay_trades</c>, and the ledger's trial kind <c>system_replay</c> (the one EF-visible change, a check constraint). The tables are raw SQL: no EF entity maps them,
    /// so the model snapshot changes only by the constraint. Insert-only for the runtime role except five columns of a run (state, cursor, result hash, end time, failure reason); a final run stays final and keeps its identity (a trigger). Rows are kept until the owner deletes a run (no delete path in this slice).
    /// </summary>
    public partial class AddSystemReplay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_test_ledger_trial",
                schema: "lab",
                table: "test_ledger");

            migrationBuilder.AddCheckConstraint(
                name: "ck_test_ledger_trial",
                schema: "lab",
                table: "test_ledger",
                sql: "trial_kind IN ('run','replan','null','training_sweep','system_replay') AND extra_trials >= 0");

            migrationBuilder.Sql("""
                CREATE TABLE research.replay_runs (
                    id uuid NOT NULL,
                    pair_id uuid NULL,
                    range_from date NOT NULL,
                    range_to date NOT NULL,
                    mode varchar(16) NOT NULL,
                    params jsonb NOT NULL,
                    settings_snapshot jsonb NOT NULL,
                    input_hash char(64) NOT NULL,
                    scope_hash char(64) NOT NULL,
                    result_hash char(64) NULL,
                    state varchar(12) NOT NULL,
                    cursor_session date NULL,
                    sessions_total integer NOT NULL,
                    created_at timestamptz NOT NULL,
                    finished_at timestamptz NULL,
                    fail_reason varchar(400) NULL,
                    CONSTRAINT pk_replay_runs PRIMARY KEY (id),
                    CONSTRAINT ck_replay_runs_state CHECK (state IN ('queued', 'running', 'cancelling', 'completed', 'failed', 'cancelled')),
                    CONSTRAINT ck_replay_runs_mode CHECK (mode IN ('frozen', 'learning')),
                    CONSTRAINT ck_replay_runs_shape CHECK (
                        range_from <= range_to AND sessions_total >= 0
                        AND input_hash ~ '^[0-9a-f]{64}$' AND scope_hash ~ '^[0-9a-f]{64}$' AND (result_hash IS NULL OR result_hash ~ '^[0-9a-f]{64}$')
                        AND (result_hash IS NULL OR state = 'completed') AND (state = 'completed') = (result_hash IS NOT NULL)
                        AND (state = 'failed') = (fail_reason IS NOT NULL)
                        AND (state IN ('completed', 'failed', 'cancelled')) = (finished_at IS NOT NULL))
                )
                """);
            migrationBuilder.Sql("CREATE INDEX ix_replay_runs_range ON research.replay_runs (range_from, range_to, created_at)");
            migrationBuilder.Sql("CREATE INDEX ix_replay_runs_pair ON research.replay_runs (pair_id) WHERE pair_id IS NOT NULL");
            migrationBuilder.Sql("CREATE INDEX ix_replay_runs_state ON research.replay_runs (state, created_at) WHERE state IN ('queued', 'running', 'cancelling')");
            migrationBuilder.Sql("""
                CREATE TABLE research.replay_sessions (
                    run_id uuid NOT NULL,
                    session date NOT NULL,
                    record jsonb NOT NULL,
                    equity numeric NOT NULL,
                    drawdown numeric NULL,
                    record_sha256 char(64) NOT NULL,
                    CONSTRAINT pk_replay_sessions PRIMARY KEY (run_id, session),
                    CONSTRAINT fk_replay_sessions_run FOREIGN KEY (run_id) REFERENCES research.replay_runs (id) ON DELETE RESTRICT,
                    CONSTRAINT ck_replay_sessions_hash CHECK (record_sha256 ~ '^[0-9a-f]{64}$')
                )
                """);
            migrationBuilder.Sql("""
                CREATE TABLE research.replay_trades (
                    run_id uuid NOT NULL,
                    trade_no bigint NOT NULL,
                    symbol varchar(40) NOT NULL,
                    side varchar(4) NOT NULL,
                    cell_key text NOT NULL,
                    qty integer NOT NULL,
                    entry_session date NOT NULL,
                    entry_at timestamptz NOT NULL,
                    entry_price numeric NOT NULL,
                    stop_price numeric NOT NULL,
                    swing boolean NOT NULL,
                    trade_key text NOT NULL,
                    exit_session date NULL,
                    exit_at timestamptz NULL,
                    gross_pnl numeric NULL,
                    charges numeric NULL,
                    net_pnl numeric NULL,
                    charge_sheet_id varchar(64) NULL,
                    r_sim double precision NULL,
                    CONSTRAINT pk_replay_trades PRIMARY KEY (run_id, trade_no),
                    CONSTRAINT fk_replay_trades_run FOREIGN KEY (run_id) REFERENCES research.replay_runs (id) ON DELETE RESTRICT,
                    CONSTRAINT ck_replay_trades_shape CHECK (side IN ('BUY', 'SELL') AND qty > 0 AND (exit_at IS NULL) = (exit_session IS NULL) AND (exit_at IS NULL) = (gross_pnl IS NULL) AND (exit_at IS NULL) = (r_sim IS NULL))
                )
                """);

            // A final run stays final and every run keeps what it was started with: the runtime role cannot reopen or rewrite it.
            migrationBuilder.Sql("""
                CREATE FUNCTION research.guard_replay_run() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    IF OLD.state IN ('completed', 'failed', 'cancelled') THEN
                        RAISE EXCEPTION 'A finished replay run is not changed' USING ERRCODE = '23514';
                    END IF;

                    IF NEW.id <> OLD.id OR NEW.pair_id IS DISTINCT FROM OLD.pair_id OR NEW.range_from <> OLD.range_from OR NEW.range_to <> OLD.range_to OR NEW.mode <> OLD.mode OR NEW.params <> OLD.params
                       OR NEW.settings_snapshot <> OLD.settings_snapshot OR NEW.input_hash <> OLD.input_hash OR NEW.scope_hash <> OLD.scope_hash OR NEW.sessions_total <> OLD.sessions_total OR NEW.created_at <> OLD.created_at THEN
                        RAISE EXCEPTION 'A replay run keeps what it was started with' USING ERRCODE = '23514';
                    END IF;

                    RETURN NEW;
                END
                $fn$
                """);
            migrationBuilder.Sql("CREATE TRIGGER trg_replay_runs_guard BEFORE UPDATE ON research.replay_runs FOR EACH ROW EXECUTE FUNCTION research.guard_replay_run()");

            // Per table, not per schema: the other research tables stay insert-only (ADR 0045). The runtime role reads and inserts; it updates five columns of a run and never deletes.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_app') THEN
                        REVOKE UPDATE, DELETE, TRUNCATE ON research.replay_runs, research.replay_sessions, research.replay_trades FROM xd_app;
                        GRANT SELECT, INSERT ON research.replay_runs, research.replay_sessions, research.replay_trades TO xd_app;
                    END IF;
                END $$;
                """);
            migrationBuilder.GrantColumnUpdateToAppRole(Schemas.Research, "replay_runs", "state", "cursor_session", "result_hash", "finished_at", "fail_reason");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS research.replay_trades");
            migrationBuilder.Sql("DROP TABLE IF EXISTS research.replay_sessions");
            migrationBuilder.Sql("DROP TABLE IF EXISTS research.replay_runs");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS research.guard_replay_run()");
            migrationBuilder.Sql("DELETE FROM lab.test_ledger WHERE trial_kind = 'system_replay';");

            migrationBuilder.DropCheckConstraint(
                name: "ck_test_ledger_trial",
                schema: "lab",
                table: "test_ledger");

            migrationBuilder.AddCheckConstraint(
                name: "ck_test_ledger_trial",
                schema: "lab",
                table: "test_ledger",
                sql: "trial_kind IN ('run','replan','null','training_sweep') AND extra_trials >= 0");
        }
    }
}
