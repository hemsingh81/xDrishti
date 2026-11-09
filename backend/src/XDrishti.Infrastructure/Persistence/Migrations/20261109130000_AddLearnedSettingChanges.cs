using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The change log of learned settings (spec AU-10, ADR 0242, ADR 0247): schema <c>config</c> with <c>learned_setting_changes</c>. Insert-only for the runtime role; the only update it may make moves the state of a row (proposed to applied or dismissed, applied to undone) and
    /// sets the decision columns. The database itself refuses what the application must not do: an automatic decision that is not a tightening made by learning, an automatic decision while <c>auto.enabled</c> is not stored as true, a loosening decided by anything but an owner,
    /// a decided row decided again, any change to what was proposed, and any delete. Raw SQL: no EF entity maps the table, so the model snapshot does not change.
    /// </summary>
    public partial class AddLearnedSettingChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE SCHEMA IF NOT EXISTS config");
            migrationBuilder.Sql("""
                CREATE TABLE config.learned_setting_changes (
                    id bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
                    setting_key varchar(120) NOT NULL,
                    old_value varchar(40) NOT NULL,
                    new_value varchar(40) NOT NULL,
                    direction varchar(7) NOT NULL,
                    source varchar(8) NOT NULL,
                    state varchar(9) NOT NULL DEFAULT 'proposed',
                    decided_by varchar(100) NULL,
                    decided_at timestamptz NULL,
                    undone_by varchar(100) NULL,
                    undone_at timestamptz NULL,
                    snapshot jsonb NOT NULL,
                    snapshot_hash char(64) NOT NULL,
                    evidence_ref varchar(300) NOT NULL,
                    evidence_hash varchar(80) NOT NULL,
                    undo_of bigint NULL,
                    created_at timestamptz NOT NULL DEFAULT now(),
                    CONSTRAINT pk_learned_setting_changes PRIMARY KEY (id),
                    CONSTRAINT fk_learned_setting_changes_undo_of FOREIGN KEY (undo_of) REFERENCES config.learned_setting_changes (id) ON DELETE RESTRICT,
                    CONSTRAINT ck_learned_setting_changes_shape CHECK (
                        direction IN ('tighten', 'loosen') AND source IN ('learning', 'owner') AND state IN ('proposed', 'applied', 'dismissed', 'undone')
                        AND snapshot_hash ~ '^[0-9a-f]{64}$' AND jsonb_typeof(snapshot) = 'object' AND length(btrim(setting_key)) > 0 AND length(btrim(evidence_hash)) > 0 AND length(btrim(evidence_ref)) > 0
                        AND old_value <> new_value AND (undo_of IS NULL) = (source = 'learning')
                    ),
                    CONSTRAINT ck_learned_setting_changes_decision CHECK (
                        ((state = 'proposed') = (decided_by IS NULL)) AND ((state = 'proposed') = (decided_at IS NULL))
                        AND (decided_by IS NULL OR decided_by = 'auto' OR (decided_by LIKE 'owner:%' AND length(btrim(decided_by)) > 6))
                        AND NOT (decided_by = 'auto' AND (direction <> 'tighten' OR source <> 'learning'))
                        AND NOT (state = 'dismissed' AND decided_by = 'auto')
                        AND ((state = 'undone') = (undone_by IS NOT NULL)) AND ((state = 'undone') = (undone_at IS NOT NULL))
                        AND (undone_by IS NULL OR (undone_by LIKE 'owner:%' AND length(btrim(undone_by)) > 6))
                    )
                )
                """);
            migrationBuilder.Sql("CREATE UNIQUE INDEX uq_learned_setting_changes_key_evidence ON config.learned_setting_changes (setting_key, evidence_hash)");
            migrationBuilder.Sql("CREATE INDEX ix_learned_setting_changes_state_id ON config.learned_setting_changes (state, id DESC)");
            migrationBuilder.Sql("CREATE INDEX ix_learned_setting_changes_key_id ON config.learned_setting_changes (setting_key, id DESC)");
            migrationBuilder.Sql("""
                CREATE FUNCTION config.guard_learned_setting_change() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    IF NEW.id <> OLD.id OR NEW.setting_key <> OLD.setting_key OR NEW.old_value <> OLD.old_value OR NEW.new_value <> OLD.new_value OR NEW.direction <> OLD.direction OR NEW.source <> OLD.source
                       OR NEW.snapshot <> OLD.snapshot OR NEW.snapshot_hash <> OLD.snapshot_hash OR NEW.evidence_ref <> OLD.evidence_ref OR NEW.evidence_hash <> OLD.evidence_hash
                       OR NEW.undo_of IS DISTINCT FROM OLD.undo_of OR NEW.created_at <> OLD.created_at THEN
                        RAISE EXCEPTION 'A learned setting change keeps what was proposed' USING ERRCODE = '23514';
                    END IF;

                    IF OLD.state = 'proposed' THEN
                        IF NEW.state NOT IN ('applied', 'dismissed') THEN
                            RAISE EXCEPTION 'A proposed change moves to applied or dismissed' USING ERRCODE = '23514';
                        END IF;

                        IF NEW.decided_by = 'auto' AND NOT EXISTS (SELECT 1 FROM platform.app_settings WHERE key = 'auto.enabled' AND value = 'true') THEN
                            RAISE EXCEPTION 'An automatic decision needs the Auto switch to be ON' USING ERRCODE = '23514';
                        END IF;

                        IF NEW.undone_by IS NOT NULL OR NEW.undone_at IS NOT NULL THEN
                            RAISE EXCEPTION 'A change that was never applied is not undone' USING ERRCODE = '23514';
                        END IF;

                        NEW.decided_at := now();
                        RETURN NEW;
                    END IF;

                    IF OLD.state = 'applied' AND NEW.state = 'undone' THEN
                        IF NEW.decided_by IS DISTINCT FROM OLD.decided_by OR NEW.decided_at IS DISTINCT FROM OLD.decided_at THEN
                            RAISE EXCEPTION 'The decision of an applied change is not rewritten' USING ERRCODE = '23514';
                        END IF;

                        NEW.undone_at := now();
                        RETURN NEW;
                    END IF;

                    RAISE EXCEPTION 'A decided learned setting change is not changed again' USING ERRCODE = '23514';
                END
                $fn$
                """);
            migrationBuilder.Sql("CREATE TRIGGER trg_learned_setting_changes_guard BEFORE UPDATE ON config.learned_setting_changes FOR EACH ROW EXECUTE FUNCTION config.guard_learned_setting_change()");

            migrationBuilder.GrantSchemaUsageToAppRole(Schemas.Config);
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Config, "learned_setting_changes");
            migrationBuilder.GrantColumnUpdateToAppRole(Schemas.Config, "learned_setting_changes", "state", "decided_by", "decided_at", "undone_by", "undone_at");
            // The runtime role can only insert an open proposal: INSERT is on the columns of the proposal itself, so it cannot name state, decided_by, decided_at or the undone columns.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_app') THEN
                        REVOKE INSERT ON config.learned_setting_changes FROM xd_app;
                        GRANT INSERT (setting_key, old_value, new_value, direction, source, snapshot, snapshot_hash, evidence_ref, evidence_hash, undo_of) ON config.learned_setting_changes TO xd_app;
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP SCHEMA IF EXISTS config CASCADE");
        }
    }
}
