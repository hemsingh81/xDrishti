using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The read-only access of the assistant's tool server (spec P9-02, ADR 0137): schema <c>ai</c> with the insert-only <c>ai.settings</c>, the function <c>ai.context_allows</c>, the two read-only roles <c>xd_mcp_read</c> and <c>xd_mcp_sql</c> (NOLOGIN here; <c>xd-api migrate</c> gives
    /// them a login when the password files exist) and schema <c>aiv</c> with the reviewed views, the only thing the roles may read. Hand-written parts: the views are the output of one list that also produced <c>DataManifest</c>; the migration is frozen once applied.
    /// </summary>
    public partial class AddAiReadAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ai");

            migrationBuilder.CreateTable(
                name: "settings",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    portfolio_context = table.Column<bool>(type: "boolean", nullable: false),
                    max_tool_calls = table.Column<int>(type: "integer", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    changed_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    snapshot_hash = table.Column<string>(type: "character(64)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_settings", x => x.id);
                    table.CheckConstraint("ck_ai_settings_shape", "max_tool_calls >= 1 AND max_tool_calls <= 12 AND changed_by LIKE 'owner:%' AND length(btrim(changed_by)) > 6 AND length(btrim(reason)) >= 1 AND length(reason) <= 500");
                });

            // The runtime role reads and inserts settings and can neither change nor delete one (the same pattern as workflow.autonomy_settings, ADR 0091).
            migrationBuilder.GrantSchemaUsageToAppRole("ai");
            migrationBuilder.MakeAppendOnlyForAppRole("ai", "settings");

            // 1. The database stamps a settings row: its id (drawn after the lock, so id order is commit order), the time, and the hash of the canonical text of EVERY value column. A later migration that adds a value column replaces this function in the same migration
            //    (a test flips each value column in turn and fails when the hash ignores one).
            migrationBuilder.Sql("""
                CREATE FUNCTION ai.stamp_settings() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    PERFORM pg_advisory_xact_lock(hashtextextended('ai.settings', 0));
                    NEW.id := nextval(pg_get_serial_sequence('ai.settings', 'id'));
                    NEW.changed_at := clock_timestamp();
                    NEW.snapshot_hash := encode(sha256(convert_to(format('ai-settings:v1|enabled=%s|portfolio_context=%s|max_tool_calls=%s', NEW.enabled::text, NEW.portfolio_context::text, NEW.max_tool_calls::text), 'UTF8')), 'hex');
                    RETURN NEW;
                END
                $fn$
                """);
            migrationBuilder.Sql("CREATE TRIGGER trg_ai_settings_stamp BEFORE INSERT ON ai.settings FOR EACH ROW EXECUTE FUNCTION ai.stamp_settings()");

            // 2. The switch lives in the database: what a view shows, and whether the assistant is on, follow the newest settings row at once. No row, any other key, or a row that is off: false. SECURITY DEFINER so that the roles need no right on ai.settings for it.
            migrationBuilder.Sql("""
                CREATE FUNCTION ai.context_allows(context_key text) RETURNS boolean LANGUAGE sql STABLE SECURITY DEFINER SET search_path = ai, pg_temp AS $fn$
                    SELECT COALESCE((SELECT CASE context_key WHEN 'assistant' THEN s.enabled WHEN 'portfolio' THEN (s.enabled AND s.portfolio_context) ELSE false END FROM ai.settings s ORDER BY s.id DESC LIMIT 1), false)
                $fn$
                """);
            migrationBuilder.Sql("REVOKE ALL ON FUNCTION ai.context_allows(text) FROM PUBLIC");

            // 3. The two roles, NOLOGIN. Created only when the migrating role may create roles (the owner of the official image is a superuser); otherwise a notice is raised and xd-api migrate reports that the roles are missing.
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    r text;
                BEGIN
                    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = current_user AND (rolsuper OR rolcreaterole)) THEN
                        RAISE NOTICE 'The roles xd_mcp_read and xd_mcp_sql were not created: the migrating role cannot create roles.';
                        RETURN;
                    END IF;
                    FOREACH r IN ARRAY ARRAY['xd_mcp_read', 'xd_mcp_sql'] LOOP
                        IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = r) THEN
                            EXECUTE format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', r);
                        END IF;
                        EXECUTE format('ALTER ROLE %I SET default_transaction_read_only = on', r);
                        EXECUTE format('ALTER ROLE %I SET lock_timeout = ''1s''', r);
                        EXECUTE format('ALTER ROLE %I SET idle_in_transaction_session_timeout = ''10s''', r);
                        EXECUTE format('ALTER ROLE %I SET work_mem = ''16MB''', r);
                        EXECUTE format('ALTER ROLE %I SET search_path = ''aiv''', r);
                        BEGIN
                            EXECUTE format('ALTER ROLE %I SET temp_file_limit = ''64MB''', r);
                        EXCEPTION WHEN insufficient_privilege THEN
                            RAISE NOTICE 'temp_file_limit of % was not set: the migrating role is not a superuser.', r;
                        END;
                    END LOOP;
                    ALTER ROLE xd_mcp_sql SET statement_timeout = '5s';
                    ALTER ROLE xd_mcp_sql CONNECTION LIMIT 3;
                    ALTER ROLE xd_mcp_read SET statement_timeout = '10s';
                    ALTER ROLE xd_mcp_read CONNECTION LIMIT 6;
                END $$
                """);

            // 4. The only helper outside aiv: the account id becomes the account LABEL (the name), inside the views; the table of accounts is never exposed. Nobody but the view owner may read it.
            migrationBuilder.Sql("CREATE VIEW ai.account_labels AS SELECT id, name AS label FROM accounts.broker_accounts");
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_app') THEN
                        REVOKE ALL ON ai.account_labels FROM xd_app;
                    END IF;
                END $$
                """);
            migrationBuilder.Sql("REVOKE ALL ON ai.account_labels FROM PUBLIC");

            // 5. The reviewed views (schema aiv). Owner-executed (security_invoker left false) so the roles need no right on a base table; every view that carries the portfolio predicate is a security barrier, so that a user-supplied function cannot see a row before the
            //    predicate filters it. Each view names its columns; a new base column is not exposed.
            migrationBuilder.Sql("CREATE SCHEMA aiv");
            migrationBuilder.Sql("""
                CREATE VIEW aiv.instruments AS
                    SELECT
                        t.id AS id,
                        t.canonical_symbol AS symbol,
                        t.exchange AS exchange,
                        t.exchange_segment AS segment,
                        t.type AS type,
                        t.name AS name,
                        t.display_name AS display_name,
                        t.isin AS isin,
                        t.series AS series,
                        t.lot_size AS lot_size,
                        t.tick_size AS tick_size,
                        t.sector AS sector,
                        t.fno_eligible AS fno_eligible,
                        t.first_seen_at AS first_seen_at,
                        t.last_seen_at AS last_seen_at,
                        t.retired_at AS retired_at
                    FROM market.instruments t
                """);

            migrationBuilder.Sql("""
                CREATE VIEW aiv.bars AS
                    SELECT
                        i.canonical_symbol AS symbol,
                        t.timeframe AS timeframe,
                        t.ts AS ts,
                        (t.ts AT TIME ZONE 'Asia/Kolkata')::date AS trade_date,
                        t.open AS open,
                        t.high AS high,
                        t.low AS low,
                        t.close AS close,
                        t.volume AS volume,
                        t.bars AS bars,
                        t.complete AS complete
                    FROM market.bars_agg t JOIN market.instruments i ON i.id = t.instrument_id
                """);

            migrationBuilder.Sql("""
                CREATE VIEW aiv.trading_days AS
                    SELECT
                        t.date AS trade_date,
                        t.kind AS kind,
                        t.session_from AS session_from,
                        t.session_to AS session_to,
                        t.source AS source,
                        t.status AS status,
                        t.note AS note
                    FROM market.trading_days t
                """);

            migrationBuilder.Sql("""
                CREATE VIEW aiv.baskets AS
                    SELECT
                        t.id AS id,
                        t.name AS name,
                        t.purpose AS purpose,
                        t.description AS description,
                        t.position AS position,
                        t.created_at AS created_at,
                        t.archived_at AS archived_at,
                        t.revision AS revision
                    FROM market.baskets t
                """);

            migrationBuilder.Sql("""
                CREATE VIEW aiv.basket_members AS
                    SELECT
                        t.basket_id AS basket_id,
                        b.name AS basket,
                        i.canonical_symbol AS symbol,
                        t.position AS position,
                        t.valid_from AS valid_from,
                        t.valid_to AS valid_to,
                        t.note AS note
                    FROM market.basket_members t JOIN market.baskets b ON b.id = t.basket_id JOIN market.instruments i ON i.id = t.instrument_id
                """);

            migrationBuilder.Sql("""
                CREATE VIEW aiv.strategies AS
                    SELECT
                        t.id AS id,
                        t.style AS style,
                        t.built_in AS built_in,
                        t.created_at AS created_at,
                        (SELECT max(v.version) FROM lab.strategy_versions v WHERE v.strategy_id = t.id) AS latest_version
                    FROM lab.strategies t
                """);

            migrationBuilder.Sql("""
                CREATE VIEW aiv.strategy_versions AS
                    SELECT
                        t.strategy_id AS strategy_id,
                        t.version AS version,
                        t.schema_version AS schema_version,
                        t.hash AS hash,
                        t.parent_hash AS parent_hash,
                        t.note AS note,
                        length(t.source_text) AS text_length,
                        t.created_at AS created_at
                    FROM lab.strategy_versions t
                """);

            migrationBuilder.Sql("""
                CREATE VIEW aiv.sim_runs AS
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
                CREATE VIEW aiv.sim_trades AS
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
                CREATE VIEW aiv.sim_trade_legs AS
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

            migrationBuilder.Sql("""
                CREATE VIEW aiv.lab_runs AS
                    SELECT
                        t.key AS lab_run_key,
                        t.strategy_id AS strategy_id,
                        t.version AS version,
                        t.strategy_hash AS strategy_hash,
                        t.window_from AS window_from,
                        t.window_to AS window_to,
                        t.method AS method,
                        t.config_hash AS config_hash,
                        t.engine_version AS engine_version,
                        t.state AS state,
                        t.stage AS stage,
                        t.progress_done AS progress_done,
                        t.progress_total AS progress_total,
                        t.sim_run_keys AS sim_run_keys,
                        t.created_at AS created_at,
                        t.started_at AS started_at,
                        t.finished_at AS finished_at
                    FROM lab.lab_runs t
                """);

            migrationBuilder.Sql("""
                CREATE VIEW aiv.lab_results AS
                    SELECT
                        r.key AS lab_run_key,
                        t.strategy_id AS strategy_id,
                        t.strategy_hash AS strategy_hash,
                        t.method AS method,
                        t.outcome AS outcome,
                        t.variant_count AS variant_count,
                        t.adjustment_factor AS adjustment_factor,
                        t.created_at AS created_at
                    FROM lab.lab_results t JOIN lab.lab_runs r ON r.id = t.lab_run_id
                """);

            migrationBuilder.Sql("""
                CREATE VIEW aiv.test_ledger AS
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
                CREATE VIEW aiv.cell_stats AS
                    SELECT
                        t.snapshot_key AS snapshot_key,
                        t.as_of AS as_of,
                        t.cell_key AS cell_key,
                        t.strategy AS strategy,
                        t.timeframe AS timeframe,
                        t.side AS side,
                        t.style AS style,
                        t.exit_policy AS exit_policy,
                        t.regime AS regime,
                        t.window_code AS window_code,
                        t.costed AS costed,
                        t.n AS n,
                        t.wins AS wins,
                        t.effective_n AS effective_n,
                        t.win_rate_raw AS win_rate_raw,
                        t.win_rate_posterior AS win_rate_posterior,
                        t.win_rate_lower AS win_rate_lower,
                        t.win_rate_upper AS win_rate_upper,
                        t.avg_win_r AS avg_win_r,
                        t.avg_loss_r AS avg_loss_r,
                        t.expectancy_r AS expectancy_r,
                        t.profit_factor AS profit_factor,
                        t.max_drawdown_r AS max_drawdown_r,
                        t.enough AS enough,
                        t.created_at AS created_at
                    FROM research.cell_stats t
                """);

            migrationBuilder.Sql("""
                CREATE VIEW aiv.model_versions AS
                    SELECT
                        t.version_key AS version_key,
                        t.sha256 AS sha256,
                        t.trainer AS trainer,
                        t.note AS note,
                        t.feature_version AS feature_version,
                        t.fit_rows AS fit_rows,
                        t.calibration_rows AS calibration_rows,
                        t.from_date AS from_date,
                        t.to_date AS to_date,
                        t.hold_out_start AS hold_out_start,
                        t.verdict AS verdict,
                        t.used_folds AS used_folds,
                        t.folds_beating_both AS folds_beating_both,
                        t.folds_needed AS folds_needed,
                        t.pooled_beats_both AS pooled_beats_both,
                        t.pooled_ece_pp AS pooled_ece_pp,
                        t.pooled_log_loss AS pooled_log_loss,
                        t.pooled_brier AS pooled_brier,
                        t.trials AS trials,
                        t.created_at AS created_at
                    FROM research.model_versions t
                """);

            migrationBuilder.Sql("""
                CREATE VIEW aiv.model_designations AS
                    SELECT
                        m.version_key AS version_key,
                        t.role AS role,
                        t.reason AS reason,
                        t.created_at AS created_at
                    FROM research.model_designations t JOIN research.model_versions m ON m.id = t.model_id
                """);

            migrationBuilder.Sql("""
                CREATE VIEW aiv.lessons AS
                    SELECT
                        t.lesson_id AS lesson_id,
                        t.state AS state,
                        t.from_state AS from_state,
                        t.monitor_kind AS monitor_kind,
                        t.subject AS subject,
                        t.evidence_key AS evidence_key,
                        t.problem AS problem,
                        t.experiment_id AS experiment_id,
                        t.result AS result,
                        t.experiment_outcome AS experiment_outcome,
                        t.before_mean_r AS before_mean_r,
                        t.after_mean_r AS after_mean_r,
                        t.before_total_r AS before_total_r,
                        t.after_total_r AS after_total_r,
                        t.reason AS reason,
                        t.created_at AS created_at,
                        t.is_seed AS is_seed
                    FROM research.lessons t
                """);

            migrationBuilder.Sql("""
                CREATE VIEW aiv.hypotheses AS
                    SELECT
                        t.hypothesis_id AS hypothesis_id,
                        t.state AS state,
                        t.from_state AS from_state,
                        t.source AS source,
                        t.cell_key AS cell_key,
                        t.knob_name AS knob_name,
                        t.knob_kind AS knob_kind,
                        t.baseline_value AS baseline_value,
                        t.candidate_value AS candidate_value,
                        t.rationale AS rationale,
                        t.baseline_run_key AS baseline_run_key,
                        t.candidate_run_key AS candidate_run_key,
                        t.experiment_id AS experiment_id,
                        t.reason AS reason,
                        t.created_at AS created_at,
                        t.is_seed AS is_seed
                    FROM research.hypotheses t
                """);

            migrationBuilder.Sql("""
                CREATE VIEW aiv.experiments AS
                    SELECT
                        t.experiment_key AS experiment_key,
                        t.knob_name AS knob_name,
                        t.knob_kind AS knob_kind,
                        t.baseline_value AS baseline_value,
                        t.candidate_value AS candidate_value,
                        t.baseline_run_key AS baseline_run_key,
                        t.candidate_run_key AS candidate_run_key,
                        t.from_date AS from_date,
                        t.to_date AS to_date,
                        t.hold_out_start AS hold_out_start,
                        t.outcome AS outcome,
                        t.trials AS trials,
                        t.used_folds AS used_folds,
                        t.folds_better AS folds_better,
                        t.folds_needed AS folds_needed,
                        t.required_improvement_r AS required_improvement_r,
                        t.baseline_trades AS baseline_trades,
                        t.candidate_trades AS candidate_trades,
                        t.pooled_baseline_mean_r AS pooled_baseline_mean_r,
                        t.pooled_candidate_mean_r AS pooled_candidate_mean_r,
                        t.baseline_total_r AS baseline_total_r,
                        t.candidate_total_r AS candidate_total_r,
                        t.expected_live_gap_r AS expected_live_gap_r,
                        t.reasons AS reasons,
                        t.created_at AS created_at
                    FROM research.experiments t
                """);

            migrationBuilder.Sql("""
                CREATE VIEW aiv.learning_changelog AS
                    SELECT
                        t.kind AS kind,
                        t.subject_kind AS subject_kind,
                        t.subject AS subject,
                        t.from_state AS from_state,
                        t.to_state AS to_state,
                        t.reason AS reason,
                        t.before_ref AS before_ref,
                        t.after_ref AS after_ref,
                        t.paper_days AS paper_days,
                        t.reverts_id AS reverts_id,
                        t.created_at AS created_at
                    FROM research.learning_changelog t
                """);

            migrationBuilder.Sql("""
                CREATE VIEW aiv.digests AS
                    SELECT
                        t.iso_week AS iso_week,
                        t.week_start AS week_start,
                        t.week_end AS week_end,
                        t.text AS text,
                        t.created_at AS created_at
                    FROM research.digests t
                """);

            migrationBuilder.Sql("""
                CREATE VIEW aiv.plans AS
                    SELECT
                        t.plan_key AS plan_key,
                        t.for_date AS for_date,
                        t.review_cutoff AS review_cutoff,
                        t.version AS version,
                        t.input_hash AS input_hash,
                        t.basis AS basis,
                        t.provisional AS provisional,
                        t.notice AS notice,
                        t.limits AS limits,
                        t.created_at AS created_at
                    FROM trading.plans t
                """);

            migrationBuilder.Sql("""
                CREATE VIEW aiv.plan_items AS
                    SELECT
                        t.id AS item_id,
                        p.plan_key AS plan_key,
                        p.for_date AS for_date,
                        t.item_rank AS item_rank,
                        t.item_group AS item_group,
                        t.symbol AS symbol,
                        t.side AS side,
                        t.strategy_id AS strategy_id,
                        t.strategy_version_hash AS strategy_version_hash,
                        t.trigger_text AS trigger_text,
                        t.stop_text AS stop_text,
                        t.target_text AS target_text,
                        t.validity_text AS validity_text,
                        t.reasons AS reasons,
                        t.cell_key AS cell_key,
                        t.trades AS trades,
                        t.data_range AS data_range,
                        t.p_win_basis AS p_win_basis,
                        t.p_win AS p_win,
                        t.p_win_lower AS p_win_lower,
                        t.conservative_expectancy_r AS conservative_expectancy_r,
                        t.unavailable AS unavailable,
                        t.advisory_note AS advisory_note,
                        t.entry_ref_price AS entry_ref_price,
                        t.levels_session AS levels_session,
                        t.reward_risk AS reward_risk,
                        t.stop_anchor AS stop_anchor,
                        t.stop_level AS stop_level,
                        t.target_level AS target_level
                    FROM trading.plan_items t JOIN trading.plans p ON p.id = t.plan_id
                """);

            migrationBuilder.Sql("""
                CREATE VIEW aiv.plan_events AS
                    SELECT
                        p.plan_key AS plan_key,
                        t.item_id AS item_id,
                        t.from_state AS from_state,
                        t.to_state AS to_state,
                        t.reason AS reason,
                        t.created_at AS created_at
                    FROM trading.plan_events t JOIN trading.plans p ON p.id = t.plan_id
                """);

            migrationBuilder.Sql("""
                CREATE VIEW aiv.grades AS
                    SELECT
                        p.plan_key AS plan_key,
                        t.item_id AS item_id,
                        t.for_date AS for_date,
                        t.status AS status,
                        t.r AS r,
                        t.mae_r AS mae_r,
                        t.mfe_r AS mfe_r,
                        t.exit_reason AS exit_reason,
                        t.entry_price AS entry_price,
                        t.exit_price AS exit_price,
                        t.stop_price AS stop_price,
                        t.cost_basis AS cost_basis,
                        t.detail AS detail,
                        t.basis AS basis,
                        t.run_key AS run_key,
                        t.graded_at AS graded_at
                    FROM trading.grades t JOIN trading.plans p ON p.id = t.plan_id
                """);

            migrationBuilder.Sql("""
                CREATE VIEW aiv.suggestions AS
                    SELECT
                        p.plan_key AS plan_key,
                        t.kind AS kind,
                        t.subject AS subject,
                        t.text AS text,
                        t.created_at AS created_at
                    FROM workflow.suggestions t JOIN trading.plans p ON p.id = t.plan_id
                """);

            migrationBuilder.Sql("""
                CREATE VIEW aiv.quality_results AS
                    SELECT
                        t.run_id AS run_id,
                        q.started_at AS ran_at,
                        i.canonical_symbol AS symbol,
                        t.status AS status,
                        t.score AS score,
                        t.bars_checked AS bars_checked,
                        t.missing_minutes AS missing_minutes
                    FROM market.quality_results t JOIN market.quality_runs q ON q.id = t.run_id JOIN market.instruments i ON i.id = t.instrument_id
                """);

            migrationBuilder.Sql("""
                CREATE VIEW aiv.service_runs AS
                    SELECT
                        t.service_id AS service_id,
                        t.trigger AS trigger,
                        t.attempt AS attempt,
                        t.started_at AS started_at,
                        t.finished_at AS finished_at,
                        t.status AS status,
                        t.message AS message
                    FROM platform.service_runs t
                """);

            migrationBuilder.Sql("""
                CREATE VIEW aiv.holdings WITH (security_barrier = true) AS
                    SELECT
                        al.label AS account,
                        t.trading_symbol AS symbol,
                        t.exchange AS exchange,
                        t.isin AS isin,
                        t.name AS name,
                        t.qty AS qty,
                        t.avg_cost AS avg_cost,
                        t.last_price AS last_price,
                        t.last_price_at AS last_price_at,
                        t.previous_close AS previous_close,
                        t.status AS status,
                        t.first_seen_at AS first_seen_at,
                        t.closed_at AS closed_at,
                        t.synced_at AS synced_at,
                        t.instrument_id AS instrument_id
                    FROM portfolio.holdings t LEFT JOIN ai.account_labels al ON al.id = t.account_id
                    WHERE ai.context_allows('portfolio')
                """);

            migrationBuilder.Sql("""
                CREATE VIEW aiv.holding_lots WITH (security_barrier = true) AS
                    SELECT
                        al.label AS account,
                        h.trading_symbol AS symbol,
                        t.acquired_on AS acquired_on,
                        t.qty AS qty,
                        t.unit_cost AS unit_cost,
                        t.source AS source,
                        t.closed_on AS closed_on,
                        t.close_price AS close_price,
                        t.estimated AS estimated
                    FROM portfolio.holding_lots t JOIN portfolio.holdings h ON h.id = t.holding_id LEFT JOIN ai.account_labels al ON al.id = h.account_id
                    WHERE ai.context_allows('portfolio')
                """);

            migrationBuilder.Sql("""
                CREATE VIEW aiv.transactions WITH (security_barrier = true) AS
                    SELECT
                        al.label AS account,
                        t.trading_symbol AS symbol,
                        t.isin AS isin,
                        t.exchange AS exchange,
                        t.side AS side,
                        t.trade_date AS trade_date,
                        t.trade_time AS trade_time,
                        t.qty AS qty,
                        t.price AS price,
                        t.charges AS charges,
                        t.source AS source,
                        t.created_at AS created_at,
                        t.voided_at AS voided_at
                    FROM portfolio.transactions t LEFT JOIN ai.account_labels al ON al.id = t.account_id
                    WHERE ai.context_allows('portfolio')
                """);

            migrationBuilder.Sql("""
                CREATE VIEW aiv.plan_item_allocations WITH (security_barrier = true) AS
                    SELECT
                        p.plan_key AS plan_key,
                        t.item_id AS item_id,
                        al.label AS account,
                        t.mode AS mode,
                        t.quantity AS quantity
                    FROM trading.plan_item_allocations t JOIN trading.plan_items i ON i.id = t.item_id JOIN trading.plans p ON p.id = i.plan_id LEFT JOIN ai.account_labels al ON al.id = t.account_id
                    WHERE ai.context_allows('portfolio')
                """);

            migrationBuilder.Sql("""
                CREATE VIEW aiv.journal_entries WITH (security_barrier = true) AS
                    SELECT
                        t.id AS entry_id,
                        p.plan_key AS plan_key,
                        t.item_id AS item_id,
                        al.label AS account,
                        t.for_date AS for_date,
                        t.created_at AS created_at,
                        t.uncertain_reason AS uncertain_reason
                    FROM trading.journal_entries t JOIN trading.plans p ON p.id = t.plan_id LEFT JOIN ai.account_labels al ON al.id = t.account_id
                    WHERE ai.context_allows('portfolio')
                """);

            migrationBuilder.Sql("""
                CREATE VIEW aiv.journal_fills WITH (security_barrier = true) AS
                    SELECT
                        t.entry_id AS entry_id,
                        t.role AS role,
                        t.qty AS qty,
                        t.reason AS reason,
                        t.for_date AS for_date,
                        t.journaled_at AS journaled_at
                    FROM trading.journal_fills t
                    WHERE ai.context_allows('portfolio')
                """);

            migrationBuilder.Sql("""
                CREATE VIEW aiv.fill_grades WITH (security_barrier = true) AS
                    SELECT
                        t.entry_id AS entry_id,
                        t.status AS status,
                        t.qty AS qty,
                        t.entry_price AS entry_price,
                        t.exit_price AS exit_price,
                        t.plan_entry_price AS plan_entry_price,
                        t.plan_stop_price AS plan_stop_price,
                        t.plan_r AS plan_r,
                        t.slippage_price AS slippage_price,
                        t.slippage_r AS slippage_r,
                        t.gross_pnl AS gross_pnl,
                        t.charges AS charges,
                        t.r AS r,
                        t.basis AS basis,
                        t.detail AS detail,
                        t.graded_at AS graded_at
                    FROM trading.fill_grades t
                    WHERE ai.context_allows('portfolio')
                """);

            // 6. xd_app has no access to aiv. The init script gives xd_app DML on every table the owner creates in every schema, and a simple view is auto-updatable: without this revoke xd_app could UPDATE or DELETE base rows through a view and bypass the insert-only
            //    grants of research.* (AC 13a). Every later migration that creates a view in aiv ends with the same two revokes; a test checks every relation of aiv.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_app') THEN
                        REVOKE ALL ON ALL TABLES IN SCHEMA aiv FROM xd_app;
                        REVOKE ALL ON SCHEMA aiv FROM xd_app;
                    END IF;
                END $$
                """);
            migrationBuilder.Sql("REVOKE ALL ON ALL TABLES IN SCHEMA aiv FROM PUBLIC");
            migrationBuilder.Sql("REVOKE ALL ON SCHEMA aiv FROM PUBLIC");

            // 7. What the roles may do: USAGE on aiv and SELECT on its views (and on views added later), the function; the read role also reads ai.settings. Nothing on any base table.
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    r text;
                BEGIN
                    FOREACH r IN ARRAY ARRAY['xd_mcp_read', 'xd_mcp_sql'] LOOP
                        IF EXISTS (SELECT FROM pg_roles WHERE rolname = r) THEN
                            EXECUTE format('REVOKE ALL ON SCHEMA public FROM %I', r);
                            EXECUTE format('GRANT USAGE ON SCHEMA aiv TO %I', r);
                            EXECUTE format('GRANT SELECT ON ALL TABLES IN SCHEMA aiv TO %I', r);
                            EXECUTE format('ALTER DEFAULT PRIVILEGES IN SCHEMA aiv GRANT SELECT ON TABLES TO %I', r);
                            EXECUTE format('GRANT EXECUTE ON FUNCTION ai.context_allows(text) TO %I', r);
                        END IF;
                    END LOOP;
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_mcp_read') THEN
                        GRANT USAGE ON SCHEMA ai TO xd_mcp_read;
                        GRANT SELECT ON ai.settings TO xd_mcp_read;
                    END IF;
                END $$
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP SCHEMA aiv CASCADE");
            migrationBuilder.Sql("DROP VIEW ai.account_labels");
            migrationBuilder.Sql("DROP FUNCTION ai.context_allows(text)");
            migrationBuilder.DropTable(
                name: "settings",
                schema: "ai");
            migrationBuilder.Sql("DROP FUNCTION ai.stamp_settings()");
            migrationBuilder.Sql("DROP SCHEMA ai");

            // The roles are cluster-wide: dropped here only when nothing else depends on them (a database of the same server that was migrated too keeps them).
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    r text;
                BEGIN
                    FOREACH r IN ARRAY ARRAY['xd_mcp_read', 'xd_mcp_sql'] LOOP
                        IF EXISTS (SELECT FROM pg_roles WHERE rolname = r) THEN
                            BEGIN
                                EXECUTE format('DROP OWNED BY %I', r);
                                EXECUTE format('DROP ROLE %I', r);
                            EXCEPTION WHEN dependent_objects_still_exist OR insufficient_privilege THEN
                                RAISE NOTICE 'The role % was kept.', r;
                            END;
                        END IF;
                    END LOOP;
                END $$
                """);
        }
    }
}
