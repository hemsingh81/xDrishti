using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExperimentJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "experiment_jobs",
                schema: "research",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_key = table.Column<string>(type: "character(64)", nullable: false),
                    baseline_run_key = table.Column<string>(type: "character(64)", nullable: false),
                    knob_kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    knob_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    baseline_value = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    candidate_value = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    hold_out_months = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    state = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    stage = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    progress_done = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    progress_total = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    cancel_requested = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    candidate_run_key = table.Column<string>(type: "character(64)", nullable: true),
                    experiment_id = table.Column<long>(type: "bigint", nullable: true),
                    error_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    error_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    instruments = table.Column<int>(type: "integer", nullable: false),
                    replay_ms = table.Column<long>(type: "bigint", nullable: true),
                    compare_ms = table.Column<long>(type: "bigint", nullable: true),
                    other_heavy_jobs_seen = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    host = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_experiment_jobs", x => x.id);
                    table.CheckConstraint("ck_experiment_jobs_progress", "progress_done >= 0 AND progress_total >= 0");
                    table.CheckConstraint("ck_experiment_jobs_shape", "job_key ~ '^[0-9a-f]{64}$' AND baseline_run_key ~ '^[0-9a-f]{64}$' AND knob_kind IN ('exit', 'slippage') AND created_by LIKE 'owner:%' AND length(btrim(created_by)) > 6 AND length(btrim(reason)) > 0 AND hold_out_months BETWEEN 0 AND 120 AND instruments >= 1");
                    table.CheckConstraint("ck_experiment_jobs_state", "state IN ('queued', 'running', 'succeeded', 'failed', 'cancelled', 'interrupted')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_experiment_jobs_job_key",
                schema: "research",
                table: "experiment_jobs",
                columns: new[] { "job_key", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_experiment_jobs_queued",
                schema: "research",
                table: "experiment_jobs",
                column: "created_at",
                filter: "state = 'queued'");

            // The foreign keys: a job is tied to the stored runs it compares and to the experiment it produced; none of them can be deleted (the runtime role has no DELETE anywhere here).
            migrationBuilder.Sql("ALTER TABLE research.experiment_jobs ADD CONSTRAINT fk_experiment_jobs_baseline FOREIGN KEY (baseline_run_key) REFERENCES research.sim_runs (run_key) ON DELETE RESTRICT");
            migrationBuilder.Sql("ALTER TABLE research.experiment_jobs ADD CONSTRAINT fk_experiment_jobs_candidate FOREIGN KEY (candidate_run_key) REFERENCES research.sim_runs (run_key) ON DELETE RESTRICT");
            migrationBuilder.Sql("ALTER TABLE research.experiment_jobs ADD CONSTRAINT fk_experiment_jobs_experiment FOREIGN KEY (experiment_id) REFERENCES research.experiments (id) ON DELETE RESTRICT");

            // ONE job at a time: at most one row in an active state, whatever the application does (two posts racing leave one).
            migrationBuilder.Sql("CREATE UNIQUE INDEX uq_experiment_jobs_one_active ON research.experiment_jobs ((true)) WHERE state IN ('queued', 'running')");

            // The third layer of the gate (after the endpoint and the handler): a job is inserted only as queued and only while the newest row of the capability log says experiments_ui is on (no row means off). A final state never changes; a running job never goes back to queued.
            migrationBuilder.Sql("""
                CREATE FUNCTION research.guard_experiment_job() RETURNS trigger LANGUAGE plpgsql AS $fn$
                DECLARE
                    gate boolean;
                BEGIN
                    IF TG_OP = 'INSERT' THEN
                        SELECT c.enabled INTO gate FROM platform.capability_changes c WHERE c.capability = 'experiments_ui' ORDER BY c.id DESC LIMIT 1;
                        IF gate IS DISTINCT FROM true THEN
                            RAISE EXCEPTION 'A replay job can be started only while experiments_ui is switched on' USING ERRCODE = '23514';
                        END IF;
                        IF NEW.state <> 'queued' THEN
                            RAISE EXCEPTION 'A replay job starts queued' USING ERRCODE = '23514';
                        END IF;
                        RETURN NEW;
                    END IF;
                    IF OLD.state IN ('succeeded', 'failed', 'cancelled', 'interrupted') THEN
                        RAISE EXCEPTION 'A replay job in a final state is not changed' USING ERRCODE = '23514';
                    END IF;
                    IF OLD.state = 'running' AND NEW.state = 'queued' THEN
                        RAISE EXCEPTION 'A running replay job does not go back to queued' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END
                $fn$
                """);
            migrationBuilder.Sql("CREATE TRIGGER trg_experiment_jobs_guard BEFORE INSERT OR UPDATE ON research.experiment_jobs FOR EACH ROW EXECUTE FUNCTION research.guard_experiment_job()");

            // The runtime role inserts a job and updates only its state columns; it never deletes (the pattern of lab.lab_runs). Not a schema-wide grant: that would re-grant UPDATE and DELETE on the append-only tables of the schema (sim_runs, sim_trades, experiments).
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_app') THEN
                        REVOKE UPDATE, DELETE ON research.experiment_jobs FROM xd_app;
                        GRANT SELECT, INSERT ON research.experiment_jobs TO xd_app;
                        GRANT UPDATE (state, stage, progress_done, progress_total, cancel_requested, candidate_run_key, experiment_id, error_code, error_text, replay_ms, compare_ms, other_heavy_jobs_seen, started_at, finished_at, host, updated_at) ON research.experiment_jobs TO xd_app;
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_experiment_jobs_guard ON research.experiment_jobs");
            migrationBuilder.DropTable(
                name: "experiment_jobs",
                schema: "research");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS research.guard_experiment_job()");
        }
    }
}
