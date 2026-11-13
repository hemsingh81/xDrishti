using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// CF-10e part 3 (ADR 0285, ADR 0282), raw SQL (no entity maps these tables, so the model snapshot does not change). (1) <c>research.ml_holdout_scores</c>: the figures (or the terminal outcome) of a model's one look at a hold-out block, insert-only for the runtime role, one row per model and block, in the
    /// shape of the spec contract (the key also carries the block, because HO-01 gives a model family one fresh look per block). An automatic row needs the newest <c>ml.holdout.auto_score</c> row to be ON, checked by the database. (2) <c>ml.holdout.auto_score</c> becomes a stored switch name: the
    /// shape check allows exactly one non-owner row, actor <c>system:default</c>, reason <c>ADR 0282</c>, ON, which the stamp trigger refuses from the runtime role and into a name that has a row already, so only this migration can write it and the owner's own rows always win. The seed row is inserted
    /// only when the name has no row. No row still reads OFF.
    /// </summary>
    public partial class AddMlHoldOutScores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE research.ml_switch_settings DROP CONSTRAINT ck_ml_switch_settings_shape");
            migrationBuilder.Sql("""
                ALTER TABLE research.ml_switch_settings ADD CONSTRAINT ck_ml_switch_settings_shape CHECK (
                    (name IN ('ml.influence.enabled', 'ml.auto_promote', 'ml.auto_rollback', 'ml.holdout.auto_score') AND actor LIKE 'owner:%' AND length(btrim(actor)) > 6 AND length(btrim(reason)) > 0)
                    OR (name = 'ml.holdout.auto_score' AND actor = 'system:default' AND reason = 'ADR 0282' AND enabled))
                """);
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION research.stamp_ml_switch_setting() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    PERFORM pg_advisory_xact_lock(hashtextextended('research.ml_switch_settings', 0));
                    IF NEW.actor = 'system:default' AND (current_user = 'xd_app' OR EXISTS (SELECT 1 FROM research.ml_switch_settings WHERE name = NEW.name)) THEN
                        RAISE EXCEPTION 'the system default row of a switch is written once, by the migration, into a name without rows' USING ERRCODE = '23514';
                    END IF;
                    NEW.id := nextval(pg_get_serial_sequence('research.ml_switch_settings', 'id'));
                    NEW.changed_at := now();
                    RETURN NEW;
                END
                $fn$
                """);
            migrationBuilder.Sql("""
                INSERT INTO research.ml_switch_settings (name, enabled, actor, reason)
                SELECT 'ml.holdout.auto_score', true, 'system:default', 'ADR 0282'
                WHERE NOT EXISTS (SELECT 1 FROM research.ml_switch_settings WHERE name = 'ml.holdout.auto_score')
                """);

            migrationBuilder.Sql("""
                CREATE TABLE research.ml_holdout_scores (
                    model_id bigint NOT NULL,
                    block_start date NOT NULL,
                    look_key varchar(200) NOT NULL,
                    outcome varchar(8) NOT NULL DEFAULT 'scored',
                    outcome_code varchar(80) NULL,
                    row_count integer NULL,
                    day_count integer NULL,
                    logloss_model double precision NULL,
                    logloss_baseline double precision NULL,
                    baseline_name varchar(16) NULL,
                    p_better double precision NULL,
                    low double precision NULL,
                    high double precision NULL,
                    policy_effect_r double precision NULL,
                    input_hash char(64) NOT NULL,
                    setting_hash char(64) NOT NULL,
                    mode varchar(5) NOT NULL,
                    actor varchar(100) NOT NULL,
                    scored_at timestamptz NOT NULL DEFAULT now(),
                    dropped integer NOT NULL,
                    block_end date NOT NULL,
                    look_id uuid NOT NULL,
                    setting_id bigint NULL,
                    versions_planned integer NOT NULL,
                    versions_run integer NOT NULL,
                    CONSTRAINT pk_ml_holdout_scores PRIMARY KEY (model_id, block_start),
                    CONSTRAINT uq_ml_holdout_scores_look UNIQUE (look_key, block_start),
                    CONSTRAINT fk_ml_holdout_scores_model_versions_model_id FOREIGN KEY (model_id) REFERENCES research.model_versions (id) ON DELETE RESTRICT,
                    CONSTRAINT fk_ml_holdout_scores_ml_switch_settings_setting_id FOREIGN KEY (setting_id) REFERENCES research.ml_switch_settings (id) ON DELETE RESTRICT,
                    CONSTRAINT ck_ml_holdout_scores_shape CHECK (dropped >= 0 AND block_end >= block_start AND versions_planned >= 0 AND versions_run >= 0 AND versions_run <= versions_planned AND input_hash ~ '^[0-9a-f]{64}$' AND setting_hash ~ '^[0-9a-f]{64}$'
                        AND mode IN ('owner', 'auto') AND ((mode = 'owner' AND actor LIKE 'owner:%' AND length(btrim(actor)) > 6) OR (mode = 'auto' AND actor LIKE 'system:%'))
                        AND outcome IN ('scored', 'no_rows', 'failed')
                        AND ((outcome = 'scored' AND outcome_code IS NULL AND row_count > 0 AND day_count > 0 AND logloss_model IS NOT NULL AND logloss_baseline IS NOT NULL AND baseline_name IN ('base_rate', 'cell_rate') AND policy_effect_r IS NOT NULL AND versions_run = versions_planned AND (p_better IS NULL OR p_better BETWEEN 0 AND 1))
                            OR (outcome <> 'scored' AND outcome_code IS NOT NULL AND length(btrim(outcome_code)) > 0 AND row_count IS NULL AND day_count IS NULL AND logloss_model IS NULL AND logloss_baseline IS NULL AND baseline_name IS NULL AND p_better IS NULL AND low IS NULL AND high IS NULL AND policy_effect_r IS NULL)))
                )
                """);
            migrationBuilder.Sql("""
                CREATE FUNCTION research.guard_ml_holdout_score() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    IF NEW.mode = 'auto' AND NEW.outcome = 'scored' AND NOT COALESCE((SELECT enabled FROM research.ml_switch_settings WHERE name = 'ml.holdout.auto_score' ORDER BY id DESC LIMIT 1), false) THEN
                        RAISE EXCEPTION 'An automatic hold-out score needs the newest ml.holdout.auto_score row to be ON' USING ERRCODE = '23514';
                    END IF;
                    -- a terminal row (no_rows, failed) ends a look that cannot give figures, so the gate reads it as unavailable and the release can go on; it needs no switch. The row belongs to a fresh look of the model kind at this block, with this look key and this look id: a score cannot be written for a look that was never claimed, a re-used one, or another model's
                    IF NOT EXISTS (SELECT 1 FROM lab.holdout_unlocks u WHERE u.strategy_id = NEW.look_key AND u.version = 1 AND u.kind = 'model' AND u.block_start = NEW.block_start AND NOT u.reused_window AND u.lab_run_id = NEW.look_id) THEN
                        RAISE EXCEPTION 'A hold-out score needs the fresh model look of this block that it belongs to' USING ERRCODE = '23514';
                    END IF;

                    NEW.scored_at := now();
                    RETURN NEW;
                END
                $fn$
                """);
            migrationBuilder.Sql("CREATE TRIGGER trg_ml_holdout_scores_guard BEFORE INSERT ON research.ml_holdout_scores FOR EACH ROW EXECUTE FUNCTION research.guard_ml_holdout_score()");
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Research, "ml_holdout_scores");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS research.ml_holdout_scores");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS research.guard_ml_holdout_score()");

            // Only the seed row goes. An owner's own row of the name stays, so the old shape check cannot be put back and this Down stops there: nothing is half undone (the whole migration is one transaction).
            migrationBuilder.Sql("DELETE FROM research.ml_switch_settings WHERE name = 'ml.holdout.auto_score' AND actor = 'system:default' AND reason = 'ADR 0282'");
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION research.stamp_ml_switch_setting() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    PERFORM pg_advisory_xact_lock(hashtextextended('research.ml_switch_settings', 0));
                    NEW.id := nextval(pg_get_serial_sequence('research.ml_switch_settings', 'id'));
                    NEW.changed_at := now();
                    RETURN NEW;
                END
                $fn$
                """);
            migrationBuilder.Sql("ALTER TABLE research.ml_switch_settings DROP CONSTRAINT ck_ml_switch_settings_shape");
            migrationBuilder.Sql("ALTER TABLE research.ml_switch_settings ADD CONSTRAINT ck_ml_switch_settings_shape CHECK (name IN ('ml.influence.enabled', 'ml.auto_promote', 'ml.auto_rollback') AND actor LIKE 'owner:%' AND length(btrim(actor)) > 6 AND length(btrim(reason)) > 0)");
        }
    }
}
