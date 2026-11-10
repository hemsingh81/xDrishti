using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The simulate-everything job (spec AU-12, ADR 0248): <c>research.sim_all_jobs</c> and <c>research.sim_all_chunks</c>. Raw SQL: no EF entity maps the tables, so the model snapshot does not change. At most one active job (running or paused) is
    /// enforced by a partial unique index; a chunk is unique per (job, strategy, version, instrument); the runtime role inserts and updates but never deletes, and a final job stays final (a trigger).
    /// </summary>
    public partial class AddSimAllJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE research.sim_all_jobs (
                    id uuid NOT NULL,
                    state varchar(24) NOT NULL,
                    pause_reason varchar(24) NULL,
                    window_from date NOT NULL,
                    window_to date NOT NULL,
                    hold_out_start date NOT NULL,
                    workers integer NOT NULL,
                    started_by varchar(100) NOT NULL,
                    created_at timestamptz NOT NULL,
                    updated_at timestamptz NOT NULL,
                    finished_at timestamptz NULL,
                    CONSTRAINT pk_sim_all_jobs PRIMARY KEY (id),
                    CONSTRAINT ck_sim_all_jobs_state CHECK (state IN ('running', 'paused', 'completed', 'completed_with_failures', 'cancelled')),
                    CONSTRAINT ck_sim_all_jobs_pause CHECK ((state = 'paused') = (pause_reason IS NOT NULL) AND (pause_reason IS NULL OR pause_reason IN ('owner', 'market_hours', 'low_disk', 'data_job', 'backfill'))),
                    CONSTRAINT ck_sim_all_jobs_shape CHECK (window_from <= window_to AND window_to < hold_out_start AND workers BETWEEN 1 AND 16 AND started_by LIKE 'owner:%' AND (state IN ('running', 'paused')) = (finished_at IS NULL))
                )
                """);
            migrationBuilder.Sql("CREATE UNIQUE INDEX uq_sim_all_jobs_one_active ON research.sim_all_jobs ((true)) WHERE state IN ('running', 'paused')");
            migrationBuilder.Sql("""
                CREATE TABLE research.sim_all_chunks (
                    job_id uuid NOT NULL,
                    strategy_id varchar(64) NOT NULL,
                    version integer NOT NULL,
                    instrument_id uuid NOT NULL,
                    strategy_hash char(64) NOT NULL,
                    ordinal integer NOT NULL,
                    state varchar(8) NOT NULL DEFAULT 'pending',
                    run_key char(64) NULL,
                    trades integer NULL,
                    record_bytes bigint NULL,
                    millis bigint NULL,
                    fail_code varchar(80) NULL,
                    fail_text varchar(300) NULL,
                    started_at timestamptz NULL,
                    finished_at timestamptz NULL,
                    CONSTRAINT pk_sim_all_chunks PRIMARY KEY (job_id, strategy_id, version, instrument_id),
                    CONSTRAINT fk_sim_all_chunks_job FOREIGN KEY (job_id) REFERENCES research.sim_all_jobs (id) ON DELETE RESTRICT,
                    CONSTRAINT ck_sim_all_chunks_state CHECK (state IN ('pending', 'running', 'done', 'failed')),
                    CONSTRAINT ck_sim_all_chunks_shape CHECK (strategy_hash ~ '^[0-9a-f]{64}$' AND ordinal >= 1 AND (state = 'done') = (run_key IS NOT NULL) AND (state = 'failed') = (fail_code IS NOT NULL))
                )
                """);
            migrationBuilder.Sql("CREATE INDEX ix_sim_all_chunks_pending ON research.sim_all_chunks (job_id, ordinal) WHERE state = 'pending'");
            migrationBuilder.Sql("CREATE INDEX ix_sim_all_chunks_state ON research.sim_all_chunks (job_id, state)");

            // A final job stays final: the runtime role cannot reopen a cancelled or completed job.
            migrationBuilder.Sql("""
                CREATE FUNCTION research.guard_sim_all_job() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    IF OLD.state IN ('completed', 'completed_with_failures', 'cancelled') THEN
                        RAISE EXCEPTION 'A finished simulate-everything job is not changed' USING ERRCODE = '23514';
                    END IF;

                    IF NEW.id <> OLD.id OR NEW.window_from <> OLD.window_from OR NEW.window_to <> OLD.window_to OR NEW.hold_out_start <> OLD.hold_out_start OR NEW.started_by <> OLD.started_by OR NEW.created_at <> OLD.created_at THEN
                        RAISE EXCEPTION 'A simulate-everything job keeps its window and its owner' USING ERRCODE = '23514';
                    END IF;

                    RETURN NEW;
                END
                $fn$
                """);
            migrationBuilder.Sql("CREATE TRIGGER trg_sim_all_jobs_guard BEFORE UPDATE ON research.sim_all_jobs FOR EACH ROW EXECUTE FUNCTION research.guard_sim_all_job()");

            // Per table, not per schema: sim_runs and sim_trades stay insert-only (ADR 0045). The runtime role reads, inserts, and updates only the state columns; it never deletes.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_app') THEN
                        REVOKE UPDATE, DELETE, TRUNCATE ON research.sim_all_jobs, research.sim_all_chunks FROM xd_app;
                        GRANT SELECT, INSERT ON research.sim_all_jobs, research.sim_all_chunks TO xd_app;
                    END IF;
                END $$;
                """);
            migrationBuilder.GrantColumnUpdateToAppRole(Schemas.Research, "sim_all_jobs", "state", "pause_reason", "updated_at", "finished_at");
            migrationBuilder.GrantColumnUpdateToAppRole(Schemas.Research, "sim_all_chunks", "state", "run_key", "trades", "record_bytes", "millis", "fail_code", "fail_text", "started_at", "finished_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS research.sim_all_chunks");
            migrationBuilder.Sql("DROP TABLE IF EXISTS research.sim_all_jobs");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS research.guard_sim_all_job()");
        }
    }
}
