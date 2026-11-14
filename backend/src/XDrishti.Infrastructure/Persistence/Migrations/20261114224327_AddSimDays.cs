using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The day-by-day simulation (spec SR-04 part A, ADR 0293): <c>research.sim_days_runs</c> and <c>research.sim_days_snapshots</c>. Raw SQL, no EF entity (the model snapshot does not change). Not hypertables (small). A snapshot is insert-only; the runtime role updates only the moving columns of a run
    /// (cursor, phase, auto, end, pending day, hold-out note, failure, updated time), a run keeps what it was started with and a cancelled or failed run stays final (a trigger). At most one run is active (a partial unique index). Nothing deletes.
    /// </summary>
    public partial class AddSimDays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE research.sim_days_runs (
                    id uuid NOT NULL,
                    input_hash char(64) NOT NULL,
                    params jsonb NOT NULL,
                    settings_snapshot jsonb NOT NULL,
                    versions jsonb NOT NULL,
                    learning_sources text NULL,
                    capital numeric NOT NULL,
                    mode varchar(16) NOT NULL,
                    range_start date NOT NULL,
                    range_end date NOT NULL,
                    requested_end date NOT NULL,
                    cursor_session date NOT NULL,
                    phase varchar(12) NOT NULL,
                    auto boolean NOT NULL DEFAULT false,
                    read_through date NULL,
                    holdout_note varchar(1000) NULL,
                    fail_reason varchar(400) NULL,
                    pending_session date NULL,
                    pending_record jsonb NULL,
                    pending_trades jsonb NULL,
                    pending_equity numeric NULL,
                    pending_hash char(64) NULL,
                    pending_end date NULL,
                    created_at timestamptz NOT NULL,
                    updated_at timestamptz NOT NULL,
                    CONSTRAINT pk_sim_days_runs PRIMARY KEY (id),
                    CONSTRAINT ck_sim_days_runs_phase CHECK (phase IN ('idle', 'settling', 'settled', 'complete', 'cancelled', 'error')),
                    CONSTRAINT ck_sim_days_runs_mode CHECK (mode IN ('frozen', 'learning')),
                    CONSTRAINT ck_sim_days_runs_shape CHECK (
                        capital > 0 AND range_start <= range_end AND range_end <= requested_end AND cursor_session BETWEEN range_start AND range_end
                        AND input_hash ~ '^[0-9a-f]{64}$' AND (pending_hash IS NULL OR pending_hash ~ '^[0-9a-f]{64}$')
                        AND (phase = 'settled') = (pending_session IS NOT NULL)
                        AND (pending_session IS NULL) = (pending_record IS NULL) AND (pending_session IS NULL) = (pending_trades IS NULL) AND (pending_session IS NULL) = (pending_equity IS NULL) AND (pending_session IS NULL) = (pending_hash IS NULL) AND (pending_session IS NULL) = (pending_end IS NULL)
                        AND (pending_session IS NULL OR pending_session = cursor_session)
                        AND (phase = 'error') = (fail_reason IS NOT NULL))
                )
                """);
            migrationBuilder.Sql("CREATE UNIQUE INDEX ux_sim_days_runs_active ON research.sim_days_runs ((true)) WHERE phase IN ('idle', 'settling', 'settled')");
            migrationBuilder.Sql("CREATE INDEX ix_sim_days_runs_created ON research.sim_days_runs (created_at)");
            migrationBuilder.Sql("""
                CREATE TABLE research.sim_days_snapshots (
                    run_id uuid NOT NULL,
                    session date NOT NULL,
                    record jsonb NOT NULL,
                    picks jsonb NOT NULL,
                    blocked jsonb NOT NULL,
                    trades jsonb NOT NULL,
                    equity_close numeric NOT NULL,
                    result_hash char(64) NOT NULL,
                    end_in_force date NOT NULL,
                    created_at timestamptz NOT NULL,
                    CONSTRAINT pk_sim_days_snapshots PRIMARY KEY (run_id, session),
                    CONSTRAINT fk_sim_days_snapshots_run FOREIGN KEY (run_id) REFERENCES research.sim_days_runs (id) ON DELETE RESTRICT,
                    CONSTRAINT ck_sim_days_snapshots_hash CHECK (result_hash ~ '^[0-9a-f]{64}$' AND session <= end_in_force)
                )
                """);

            // A run keeps what it was started with, and a cancelled or failed run stays final: the runtime role cannot reopen or rewrite it.
            migrationBuilder.Sql("""
                CREATE FUNCTION research.guard_sim_days_run() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    IF OLD.phase IN ('cancelled', 'error') THEN
                        RAISE EXCEPTION 'A cancelled or failed simulation is not changed' USING ERRCODE = '23514';
                    END IF;

                    IF NEW.id <> OLD.id OR NEW.input_hash <> OLD.input_hash OR NEW.params <> OLD.params OR NEW.settings_snapshot <> OLD.settings_snapshot OR NEW.versions <> OLD.versions
                       OR NEW.learning_sources IS DISTINCT FROM OLD.learning_sources OR NEW.capital <> OLD.capital OR NEW.mode <> OLD.mode OR NEW.range_start <> OLD.range_start OR NEW.created_at <> OLD.created_at
                       OR NEW.range_end < OLD.range_end OR NEW.cursor_session < OLD.cursor_session THEN
                        RAISE EXCEPTION 'A simulation keeps what it was started with and only moves forward' USING ERRCODE = '23514';
                    END IF;

                    RETURN NEW;
                END
                $fn$
                """);
            migrationBuilder.Sql("CREATE TRIGGER trg_sim_days_runs_guard BEFORE UPDATE ON research.sim_days_runs FOR EACH ROW EXECUTE FUNCTION research.guard_sim_days_run()");

            // Per table, not per schema: the other research tables stay insert-only (ADR 0045). The runtime role reads and inserts; it updates the moving columns of a run and never deletes.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_app') THEN
                        REVOKE UPDATE, DELETE, TRUNCATE ON research.sim_days_runs, research.sim_days_snapshots FROM xd_app;
                        GRANT SELECT, INSERT ON research.sim_days_runs, research.sim_days_snapshots TO xd_app;
                    END IF;
                END $$;
                """);
            migrationBuilder.GrantColumnUpdateToAppRole(
                Schemas.Research, "sim_days_runs", "range_end", "requested_end", "cursor_session", "phase", "auto", "read_through", "holdout_note", "fail_reason",
                "pending_session", "pending_record", "pending_trades", "pending_equity", "pending_hash", "pending_end", "updated_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS research.sim_days_snapshots");
            migrationBuilder.Sql("DROP TABLE IF EXISTS research.sim_days_runs");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS research.guard_sim_days_run()");
        }
    }
}
