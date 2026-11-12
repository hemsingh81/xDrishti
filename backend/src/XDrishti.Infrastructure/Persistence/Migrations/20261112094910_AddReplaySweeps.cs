using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The no-selection baseline arm and the start-date sweep of the system replay (spec SR-02, ADR 0279): <c>research.replay_sweeps</c> (insert-only), and on <c>research.replay_runs</c> the columns <c>arm</c> (selection or baseline) and <c>sweep_id</c>. Raw SQL: no EF entity maps these tables,
    /// so the model snapshot does not change. Both columns are fixed at the insert (the guard trigger is replaced to say so); the runtime role's column updates are unchanged.
    /// </summary>
    public partial class AddReplaySweeps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE research.replay_sweeps (
                    id uuid NOT NULL,
                    params jsonb NOT NULL,
                    starts date[] NOT NULL,
                    created_at timestamptz NOT NULL,
                    CONSTRAINT pk_replay_sweeps PRIMARY KEY (id),
                    CONSTRAINT ck_replay_sweeps_starts CHECK (cardinality(starts) BETWEEN 2 AND 12)
                )
                """);
            migrationBuilder.Sql("ALTER TABLE research.replay_runs ADD COLUMN arm text NOT NULL DEFAULT 'selection', ADD COLUMN sweep_id uuid NULL");
            migrationBuilder.Sql("ALTER TABLE research.replay_runs ADD CONSTRAINT ck_replay_runs_arm CHECK (arm IN ('selection', 'baseline'))");
            migrationBuilder.Sql("ALTER TABLE research.replay_runs ADD CONSTRAINT fk_replay_runs_sweep FOREIGN KEY (sweep_id) REFERENCES research.replay_sweeps (id) ON DELETE RESTRICT");
            migrationBuilder.Sql("CREATE INDEX ix_replay_runs_sweep ON research.replay_runs (sweep_id, created_at) WHERE sweep_id IS NOT NULL");

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION research.guard_replay_run() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    IF OLD.state IN ('completed', 'failed', 'cancelled') THEN
                        RAISE EXCEPTION 'A finished replay run is not changed' USING ERRCODE = '23514';
                    END IF;

                    IF NEW.id <> OLD.id OR NEW.pair_id IS DISTINCT FROM OLD.pair_id OR NEW.range_from <> OLD.range_from OR NEW.range_to <> OLD.range_to OR NEW.mode <> OLD.mode OR NEW.params <> OLD.params
                       OR NEW.settings_snapshot <> OLD.settings_snapshot OR NEW.versions <> OLD.versions OR NEW.learning_sources IS DISTINCT FROM OLD.learning_sources OR NEW.input_hash <> OLD.input_hash OR NEW.scope_hash <> OLD.scope_hash OR NEW.sessions_total <> OLD.sessions_total OR NEW.created_at <> OLD.created_at
                       OR NEW.arm <> OLD.arm OR NEW.sweep_id IS DISTINCT FROM OLD.sweep_id THEN
                        RAISE EXCEPTION 'A replay run keeps what it was started with' USING ERRCODE = '23514';
                    END IF;

                    RETURN NEW;
                END
                $fn$
                """);

            // Insert-only for the runtime role, per table (ADR 0045): the sweep is never updated or deleted.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_app') THEN
                        REVOKE UPDATE, DELETE, TRUNCATE ON research.replay_sweeps FROM xd_app;
                        GRANT SELECT, INSERT ON research.replay_sweeps TO xd_app;
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION research.guard_replay_run() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    IF OLD.state IN ('completed', 'failed', 'cancelled') THEN
                        RAISE EXCEPTION 'A finished replay run is not changed' USING ERRCODE = '23514';
                    END IF;

                    IF NEW.id <> OLD.id OR NEW.pair_id IS DISTINCT FROM OLD.pair_id OR NEW.range_from <> OLD.range_from OR NEW.range_to <> OLD.range_to OR NEW.mode <> OLD.mode OR NEW.params <> OLD.params
                       OR NEW.settings_snapshot <> OLD.settings_snapshot OR NEW.versions <> OLD.versions OR NEW.learning_sources IS DISTINCT FROM OLD.learning_sources OR NEW.input_hash <> OLD.input_hash OR NEW.scope_hash <> OLD.scope_hash OR NEW.sessions_total <> OLD.sessions_total OR NEW.created_at <> OLD.created_at THEN
                        RAISE EXCEPTION 'A replay run keeps what it was started with' USING ERRCODE = '23514';
                    END IF;

                    RETURN NEW;
                END
                $fn$
                """);
            migrationBuilder.Sql("DROP INDEX IF EXISTS research.ix_replay_runs_sweep");
            migrationBuilder.Sql("ALTER TABLE research.replay_runs DROP CONSTRAINT IF EXISTS fk_replay_runs_sweep, DROP CONSTRAINT IF EXISTS ck_replay_runs_arm");
            migrationBuilder.Sql("ALTER TABLE research.replay_runs DROP COLUMN IF EXISTS sweep_id, DROP COLUMN IF EXISTS arm");
            migrationBuilder.Sql("DROP TABLE IF EXISTS research.replay_sweeps");
        }
    }
}
