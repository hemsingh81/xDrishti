using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The loosening banner of the plan screen (spec BK-10b, ADR 0265): <c>trading.loosening_settings</c> (the owner's automatic dismissal setting, insert-only, the id drawn after an advisory lock so the highest id is the last to commit) and
    /// <c>trading.loosening_dismissals</c> (one insert-only row per loosened change; the database refuses a second one, an automatic row that does not name the newest setting row while it is ON, and any edit or delete by the runtime role).
    /// Raw SQL on purpose (nothing maps these tables in the EF model), so the model snapshot does not change; Down drops both.
    /// </summary>
    public partial class AddLooseningDismissals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE trading.loosening_settings (
                    id bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
                    auto_dismiss boolean NOT NULL,
                    changed_by varchar(256) NOT NULL,
                    changed_at timestamptz NOT NULL DEFAULT now(),
                    hash varchar(64) NOT NULL,
                    CONSTRAINT pk_loosening_settings PRIMARY KEY (id),
                    CONSTRAINT ck_loosening_settings_owner CHECK (changed_by LIKE 'owner:%' AND length(btrim(changed_by)) > 6)
                )
                """);
            migrationBuilder.Sql("""
                CREATE FUNCTION trading.stamp_loosening_setting() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    PERFORM pg_advisory_xact_lock(hashtextextended('trading.loosening_settings', 0));
                    NEW.id := nextval(pg_get_serial_sequence('trading.loosening_settings', 'id'));
                    NEW.changed_at := now();
                    RETURN NEW;
                END
                $fn$
                """);
            migrationBuilder.Sql("CREATE TRIGGER trg_loosening_settings_stamp BEFORE INSERT ON trading.loosening_settings FOR EACH ROW EXECUTE FUNCTION trading.stamp_loosening_setting()");

            migrationBuilder.Sql("""
                CREATE TABLE trading.loosening_dismissals (
                    id bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
                    notice_source varchar(8) NOT NULL,
                    notice_id bigint NOT NULL,
                    actor varchar(256) NOT NULL,
                    setting_id bigint NULL,
                    setting_hash varchar(64) NULL,
                    created_at timestamptz NOT NULL DEFAULT now(),
                    CONSTRAINT pk_loosening_dismissals PRIMARY KEY (id),
                    CONSTRAINT uq_loosening_dismissals_notice UNIQUE (notice_source, notice_id),
                    CONSTRAINT fk_loosening_dismissals_loosening_settings_setting_id FOREIGN KEY (setting_id) REFERENCES trading.loosening_settings (id) ON DELETE RESTRICT,
                    CONSTRAINT ck_loosening_dismissals_source CHECK (notice_source IN ('sizing', 'budget', 'basket') AND notice_id > 0),
                    CONSTRAINT ck_loosening_dismissals_actor CHECK (
                        (actor = 'auto') = (setting_id IS NOT NULL)
                        AND (setting_id IS NULL) = (setting_hash IS NULL)
                        AND (actor = 'auto' OR (actor LIKE 'owner:%' AND length(btrim(actor)) > 6))
                    )
                )
                """);
            migrationBuilder.Sql("""
                CREATE FUNCTION trading.guard_loosening_dismissal() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    IF NEW.actor = 'auto' AND NOT EXISTS (
                        SELECT 1 FROM trading.loosening_settings s
                        WHERE s.id = NEW.setting_id AND s.auto_dismiss AND s.hash = NEW.setting_hash AND s.id = (SELECT max(id) FROM trading.loosening_settings)) THEN
                        RAISE EXCEPTION 'An automatic dismissal needs the newest row of the setting to be ON and to be the one recorded' USING ERRCODE = '23514';
                    END IF;

                    RETURN NEW;
                END
                $fn$
                """);
            migrationBuilder.Sql("CREATE TRIGGER trg_loosening_dismissals_guard BEFORE INSERT ON trading.loosening_dismissals FOR EACH ROW EXECUTE FUNCTION trading.guard_loosening_dismissal()");

            // Insert and select only for the runtime role; the identity of the setting is drawn in a trigger that runs as the caller, which needs USAGE on its sequence.
            migrationBuilder.GrantTableToAppRole(Schemas.Trading, "loosening_settings");
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Trading, "loosening_settings");
            migrationBuilder.GrantTableToAppRole(Schemas.Trading, "loosening_dismissals");
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Trading, "loosening_dismissals");
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_app') THEN
                        EXECUTE format('GRANT USAGE ON SEQUENCE %s TO xd_app', pg_get_serial_sequence('trading.loosening_settings', 'id'));
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS trading.loosening_dismissals");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS trading.guard_loosening_dismissal()");
            migrationBuilder.Sql("DROP TABLE IF EXISTS trading.loosening_settings");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS trading.stamp_loosening_setting()");
        }
    }
}
