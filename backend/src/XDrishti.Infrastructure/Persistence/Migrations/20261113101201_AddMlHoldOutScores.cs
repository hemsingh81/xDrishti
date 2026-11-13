using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// CF-10e part 3 (ADR 0285, ADR 0282), raw SQL (no entity maps these tables, so the model snapshot does not change). (1) <c>research.ml_holdout_scores</c>: the figures of a model's one look at a hold-out block, insert-only for the runtime role, one row per model and block, in the
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
                    row_count integer NOT NULL,
                    day_count integer NOT NULL,
                    logloss_model double precision NOT NULL,
                    logloss_baseline double precision NOT NULL,
                    baseline_name varchar(16) NOT NULL,
                    p_better double precision NULL,
                    low double precision NULL,
                    high double precision NULL,
                    policy_effect_r double precision NOT NULL,
                    input_hash char(64) NOT NULL,
                    setting_hash char(64) NOT NULL,
                    mode varchar(5) NOT NULL,
                    actor varchar(100) NOT NULL,
                    scored_at timestamptz NOT NULL DEFAULT now(),
                    CONSTRAINT pk_ml_holdout_scores PRIMARY KEY (model_id, block_start),
                    CONSTRAINT fk_ml_holdout_scores_model_versions_model_id FOREIGN KEY (model_id) REFERENCES research.model_versions (id) ON DELETE RESTRICT,
                    CONSTRAINT ck_ml_holdout_scores_shape CHECK (row_count > 0 AND day_count > 0 AND baseline_name IN ('base_rate', 'cell_rate') AND input_hash ~ '^[0-9a-f]{64}$' AND setting_hash ~ '^[0-9a-f]{64}$'
                        AND (p_better IS NULL OR p_better BETWEEN 0 AND 1) AND mode IN ('owner', 'auto')
                        AND ((mode = 'owner' AND actor LIKE 'owner:%' AND length(btrim(actor)) > 6) OR (mode = 'auto' AND actor LIKE 'system:%')))
                )
                """);
            migrationBuilder.Sql("""
                CREATE FUNCTION research.guard_ml_holdout_score() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    IF NEW.mode = 'auto' AND NOT COALESCE((SELECT enabled FROM research.ml_switch_settings WHERE name = 'ml.holdout.auto_score' ORDER BY id DESC LIMIT 1), false) THEN
                        RAISE EXCEPTION 'An automatic hold-out score needs the newest ml.holdout.auto_score row to be ON' USING ERRCODE = '23514';
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

            // Fails closed once an automatic act references nothing here; the seed row is deleted only while it is the name's only row kind.
            migrationBuilder.Sql("DELETE FROM research.ml_switch_settings WHERE name = 'ml.holdout.auto_score'");
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
