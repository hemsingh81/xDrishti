using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// AU-13 (ADR 0249): training from the simulation. <c>research.sim_training_runs</c> is one row per training (its inputs hash, what was left out, the out-of-fold score beside the baseline, the model it registered as a challenger, and a CHECK that the hold-out was not read);
    /// <c>research.training_sim_cell_stats</c> holds the cell statistics on the fixed <c>training_sim</c> basis in a table of their own, so no reader of <c>research.cell_stats</c> (the research and forward basis) can ever see them. Both are insert-only for the runtime role.
    /// </summary>
    public partial class AddTrainingSimBasis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE research.sim_training_runs (
                    id bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
                    input_hash char(64) NOT NULL,
                    job_id uuid NOT NULL,
                    state varchar(16) NOT NULL,
                    not_evaluable text NULL,
                    runs integer NOT NULL,
                    runs_not_finished integer NOT NULL,
                    row_count integer NOT NULL,
                    sample_every integer NOT NULL,
                    folds_used integer NOT NULL,
                    folds_total integer NOT NULL,
                    embargo_sessions integer NOT NULL,
                    embargo_days integer NOT NULL,
                    window_from date NOT NULL,
                    window_to date NOT NULL,
                    hold_out_start date NOT NULL,
                    hold_out_read boolean NOT NULL DEFAULT false,
                    candidate_log_loss double precision NULL,
                    baseline_log_loss double precision NULL,
                    baseline_name varchar(32) NULL,
                    candidate_brier double precision NULL,
                    baseline_brier double precision NULL,
                    verdict varchar(16) NULL,
                    model_id bigint NULL,
                    training_set_id char(64) NULL,
                    summary text NOT NULL,
                    created_at timestamptz NOT NULL,
                    CONSTRAINT pk_sim_training_runs PRIMARY KEY (id),
                    CONSTRAINT uq_sim_training_runs_input_hash UNIQUE (input_hash),
                    CONSTRAINT fk_sim_training_runs_job FOREIGN KEY (job_id) REFERENCES research.sim_all_jobs (id) ON DELETE RESTRICT,
                    CONSTRAINT ck_sim_training_runs_state CHECK (state IN ('trained', 'not_evaluable')),
                    CONSTRAINT ck_sim_training_runs_shape CHECK (input_hash ~ '^[0-9a-f]{64}$' AND window_from <= window_to AND window_to < hold_out_start AND sample_every >= 1 AND embargo_sessions BETWEEN 0 AND 20
                        AND (state = 'trained') = (model_id IS NOT NULL) AND (state = 'not_evaluable') = (not_evaluable IS NOT NULL) AND (verdict IS NULL OR verdict <> 'Eligible')),
                    CONSTRAINT ck_sim_training_runs_hold_out_unread CHECK (hold_out_read = false)
                )
                """);
            migrationBuilder.Sql("CREATE INDEX ix_sim_training_runs_job ON research.sim_training_runs (job_id, created_at DESC)");
            migrationBuilder.Sql("""
                CREATE TABLE research.training_sim_cell_stats (
                    id bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
                    training_run_id bigint NOT NULL,
                    basis varchar(16) NOT NULL DEFAULT 'training_sim',
                    as_of date NOT NULL,
                    cell_key text NOT NULL,
                    strategy varchar(64) NOT NULL,
                    timeframe varchar(64) NOT NULL,
                    side varchar(64) NOT NULL,
                    window_code varchar(8) NOT NULL,
                    costed boolean NOT NULL,
                    n integer NOT NULL,
                    wins integer NOT NULL,
                    effective_n double precision NOT NULL,
                    win_rate_posterior double precision NOT NULL,
                    win_rate_lower double precision NOT NULL,
                    win_rate_upper double precision NOT NULL,
                    expectancy_r double precision NULL,
                    enough boolean NOT NULL,
                    CONSTRAINT pk_training_sim_cell_stats PRIMARY KEY (id),
                    CONSTRAINT fk_training_sim_cell_stats_run FOREIGN KEY (training_run_id) REFERENCES research.sim_training_runs (id) ON DELETE RESTRICT,
                    CONSTRAINT ck_training_sim_cell_stats_basis CHECK (basis = 'training_sim'),
                    CONSTRAINT ck_training_sim_cell_stats_shape CHECK (window_code IN ('all', '250d', '60d') AND n >= 0 AND wins >= 0 AND wins <= n),
                    CONSTRAINT uq_training_sim_cell_stats_run_cell_window UNIQUE (training_run_id, cell_key, window_code)
                )
                """);
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_app') THEN
                        REVOKE UPDATE, DELETE, TRUNCATE ON research.sim_training_runs, research.training_sim_cell_stats FROM xd_app;
                        GRANT SELECT, INSERT ON research.sim_training_runs, research.training_sim_cell_stats TO xd_app;
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS research.training_sim_cell_stats");
            migrationBuilder.Sql("DROP TABLE IF EXISTS research.sim_training_runs");
        }
    }
}
