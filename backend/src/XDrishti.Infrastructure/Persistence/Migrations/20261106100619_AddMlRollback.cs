using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The ML switches, the rollback proposals and the daily monitor record (spec CF-10 part 10d-1, ADR 0220), in schema <c>research</c> beside the models, their designations and the changelog (the schema choice of <c>AddMlSafety</c>): <c>ml_switch_settings</c> (the owner's three
    /// switches, append-only, id in commit order, default OFF because no row means OFF), <c>rollback_proposals</c> (insert, plus one transition proposed to applied or dismissed that touches four columns) and <c>ml_monitor_checks</c> (one insert-only evaluation per model and day).
    /// Raw SQL on purpose: no EF entity maps these tables (the runtime uses Npgsql), so the model snapshot does not change. The database itself refuses what the application must not do: an automatic decision while the newest <c>ml.auto_rollback</c> row is not ON, an automatic
    /// dismissal, a switch change that names no owner, a decided proposal changed again, and any edit or delete by the runtime role. The changelog needs no new column: its free-text actor says <c>auto</c> and its evidence ids carry the proposal, the switch row and the settings hash.
    /// </summary>
    public partial class AddMlRollback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. The owner's three switches, stored like confidence.kill_settings: append-only, the id drawn after an advisory lock so that the highest id of a name is the last to commit. No row of a name means OFF.
            migrationBuilder.Sql("""
                CREATE TABLE research.ml_switch_settings (
                    id bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
                    name varchar(32) NOT NULL,
                    enabled boolean NOT NULL,
                    actor varchar(100) NOT NULL,
                    reason varchar(500) NOT NULL,
                    changed_at timestamptz NOT NULL DEFAULT now(),
                    CONSTRAINT pk_ml_switch_settings PRIMARY KEY (id),
                    CONSTRAINT ck_ml_switch_settings_shape CHECK (name IN ('ml.influence.enabled', 'ml.auto_promote', 'ml.auto_rollback') AND actor LIKE 'owner:%' AND length(btrim(actor)) > 6 AND length(btrim(reason)) > 0)
                )
                """);
            migrationBuilder.Sql("CREATE INDEX ix_ml_switch_settings_name_id ON research.ml_switch_settings (name, id DESC)");
            migrationBuilder.Sql("""
                CREATE FUNCTION research.stamp_ml_switch_setting() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    PERFORM pg_advisory_xact_lock(hashtextextended('research.ml_switch_settings', 0));
                    NEW.id := nextval(pg_get_serial_sequence('research.ml_switch_settings', 'id'));
                    NEW.changed_at := now();
                    RETURN NEW;
                END
                $fn$
                """);
            migrationBuilder.Sql("CREATE TRIGGER trg_ml_switch_settings_stamp BEFORE INSERT ON research.ml_switch_settings FOR EACH ROW EXECUTE FUNCTION research.stamp_ml_switch_setting()");

            // 2. The rollback proposals. One per model, trigger and India day. The previous champion is information only: nothing here re-promotes it.
            migrationBuilder.Sql("""
                CREATE TABLE research.rollback_proposals (
                    id bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
                    model_id bigint NOT NULL,
                    trigger varchar(12) NOT NULL,
                    value double precision NULL,
                    limit_value double precision NULL,
                    text varchar(1000) NOT NULL,
                    cells jsonb NOT NULL,
                    previous_champion_id bigint NULL,
                    previous_qualifies boolean NOT NULL,
                    settings_hash char(64) NOT NULL,
                    proposed_on date NOT NULL,
                    proposed_at timestamptz NOT NULL DEFAULT now(),
                    state varchar(9) NOT NULL DEFAULT 'proposed',
                    actor varchar(100) NULL,
                    decided_at timestamptz NULL,
                    auto_setting_id bigint NULL,
                    CONSTRAINT pk_rollback_proposals PRIMARY KEY (id),
                    CONSTRAINT fk_rollback_proposals_model_versions_model_id FOREIGN KEY (model_id) REFERENCES research.model_versions (id) ON DELETE RESTRICT,
                    CONSTRAINT fk_rollback_proposals_model_versions_previous_champion_id FOREIGN KEY (previous_champion_id) REFERENCES research.model_versions (id) ON DELETE RESTRICT,
                    CONSTRAINT fk_rollback_proposals_ml_switch_settings_auto_setting_id FOREIGN KEY (auto_setting_id) REFERENCES research.ml_switch_settings (id) ON DELETE RESTRICT,
                    CONSTRAINT ck_rollback_proposals_trigger CHECK (trigger IN ('calibration', 'band')),
                    CONSTRAINT ck_rollback_proposals_shape CHECK (state IN ('proposed', 'applied', 'dismissed') AND settings_hash ~ '^[0-9a-f]{64}$' AND jsonb_typeof(cells) = 'array' AND (previous_champion_id IS NOT NULL OR NOT previous_qualifies)),
                    CONSTRAINT ck_rollback_proposals_decision CHECK (
                        ((state = 'proposed') = (actor IS NULL)) AND ((state = 'proposed') = (decided_at IS NULL))
                        AND (actor IS NULL OR actor = 'auto' OR (actor LIKE 'owner:%' AND length(btrim(actor)) > 6))
                        AND ((actor IS NOT DISTINCT FROM 'auto') = (auto_setting_id IS NOT NULL))
                        AND NOT (actor = 'auto' AND state <> 'applied')
                    )
                )
                """);
            migrationBuilder.Sql("CREATE UNIQUE INDEX uq_rollback_proposals_model_trigger_day ON research.rollback_proposals (model_id, trigger, proposed_on)");
            migrationBuilder.Sql("CREATE INDEX ix_rollback_proposals_state_id ON research.rollback_proposals (state, id DESC)");
            migrationBuilder.Sql("CREATE INDEX ix_rollback_proposals_model_id_id ON research.rollback_proposals (model_id, id DESC)");
            migrationBuilder.Sql("""
                CREATE FUNCTION research.guard_rollback_proposal() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    IF OLD.state <> 'proposed' THEN
                        RAISE EXCEPTION 'A decided rollback proposal is not changed' USING ERRCODE = '23514';
                    END IF;

                    IF NEW.state = 'proposed' THEN
                        RAISE EXCEPTION 'A rollback proposal only moves on, from proposed to applied or dismissed' USING ERRCODE = '23514';
                    END IF;

                    IF NEW.actor = 'auto' AND NOT EXISTS (
                        SELECT 1 FROM research.ml_switch_settings s
                        WHERE s.id = NEW.auto_setting_id AND s.name = 'ml.auto_rollback' AND s.enabled
                          AND s.id = (SELECT max(id) FROM research.ml_switch_settings WHERE name = 'ml.auto_rollback')) THEN
                        RAISE EXCEPTION 'An automatic decision needs the newest row of the ml.auto_rollback switch to be ON and to be the one recorded' USING ERRCODE = '23514';
                    END IF;

                    NEW.decided_at := now();
                    RETURN NEW;
                END
                $fn$
                """);
            migrationBuilder.Sql("CREATE TRIGGER trg_rollback_proposals_guard BEFORE UPDATE ON research.rollback_proposals FOR EACH ROW EXECUTE FUNCTION research.guard_rollback_proposal()");

            // 3. The evaluations of the ML monitor, one per model and India day, with the settings hash and the two trigger readings.
            migrationBuilder.Sql("""
                CREATE TABLE research.ml_monitor_checks (
                    id bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
                    model_id bigint NOT NULL,
                    checked_on date NOT NULL,
                    evaluated_at timestamptz NOT NULL DEFAULT now(),
                    settings_hash char(64) NOT NULL,
                    verdicts jsonb NOT NULL,
                    CONSTRAINT pk_ml_monitor_checks PRIMARY KEY (id),
                    CONSTRAINT fk_ml_monitor_checks_model_versions_model_id FOREIGN KEY (model_id) REFERENCES research.model_versions (id) ON DELETE RESTRICT,
                    CONSTRAINT ck_ml_monitor_checks_shape CHECK (settings_hash ~ '^[0-9a-f]{64}$' AND jsonb_typeof(verdicts) = 'array')
                )
                """);
            migrationBuilder.Sql("CREATE UNIQUE INDEX uq_ml_monitor_checks_model_day ON research.ml_monitor_checks (model_id, checked_on)");

            // Runtime role: table by table. ml_switch_settings and ml_monitor_checks are insert-only; rollback_proposals is insert-only except the four decision columns (a column grant), and nothing is ever deleted.
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Research, "ml_switch_settings");
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Research, "ml_monitor_checks");
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Research, "rollback_proposals");
            migrationBuilder.GrantColumnUpdateToAppRole(Schemas.Research, "rollback_proposals", "state", "actor", "decided_at", "auto_setting_id");

            // The runtime role can only insert an open proposal: INSERT is granted on the columns of the proposal itself, so it cannot name state, actor, decided_at or auto_setting_id (an already-decided row). The defaults make the row proposed.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_app') THEN
                        REVOKE INSERT ON research.rollback_proposals FROM xd_app;
                        GRANT INSERT (model_id, trigger, value, limit_value, text, cells, previous_champion_id, previous_qualifies, settings_hash, proposed_on) ON research.rollback_proposals TO xd_app;
                    END IF;
                END $$;
                """);
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_app') THEN
                        EXECUTE format('GRANT USAGE ON SEQUENCE %s TO xd_app', pg_get_serial_sequence('research.ml_switch_settings', 'id'));
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS research.ml_monitor_checks");
            migrationBuilder.Sql("DROP TABLE IF EXISTS research.rollback_proposals");
            migrationBuilder.Sql("DROP TABLE IF EXISTS research.ml_switch_settings");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS research.guard_rollback_proposal()");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS research.stamp_ml_switch_setting()");
        }
    }
}
