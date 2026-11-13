using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// AM-01a (ADR 0282), data only: <c>confidence.kill_settings</c> may carry the seed row of the system. The shape check now also allows exactly actor <c>system:default</c>, reason <c>ADR 0282</c>, switch ON, and the stamp trigger refuses that actor
    /// from the runtime role (<c>xd_app</c>) and into any non-empty table, so only this migration (as the owner role, into the empty table) can write it and the runtime can never turn kill-auto back on without the owner's password. The seed row is inserted only
    /// into an empty table, so the owner's own rows (either direction) always win and a lost table still reads OFF. The six services that start enabled need no row: a service with no row runs on its definition, which is now enabled, and a row the owner saved is never touched.
    /// No entity changes, so the model snapshot does not change.
    /// </summary>
    public partial class ApplyAutoModeDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE confidence.kill_settings DROP CONSTRAINT ck_kill_settings_shape");
            migrationBuilder.Sql("""
                ALTER TABLE confidence.kill_settings ADD CONSTRAINT ck_kill_settings_shape CHECK (
                    (actor LIKE 'owner:%' AND length(btrim(actor)) > 6 AND length(btrim(reason)) > 0)
                    OR (actor = 'system:default' AND auto AND reason = 'ADR 0282'))
                """);
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION confidence.stamp_kill_setting() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    PERFORM pg_advisory_xact_lock(hashtextextended('confidence.kill_settings', 0));
                    IF NEW.actor = 'system:default' AND (current_user = 'xd_app' OR EXISTS (SELECT 1 FROM confidence.kill_settings)) THEN
                        RAISE EXCEPTION 'the system default row of the kill switch is written once, by the migration, into an empty table' USING ERRCODE = '23514';
                    END IF;
                    NEW.id := nextval(pg_get_serial_sequence('confidence.kill_settings', 'id'));
                    NEW.changed_at := now();
                    RETURN NEW;
                END
                $fn$
                """);
            migrationBuilder.Sql("""
                INSERT INTO confidence.kill_settings (auto, actor, reason)
                SELECT true, 'system:default', 'ADR 0282'
                WHERE NOT EXISTS (SELECT 1 FROM confidence.kill_settings)
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Fails closed: once an automatic kill decision references the seed row (foreign key RESTRICT), the delete is refused and the rollback stops; nothing is half undone because the whole migration is one transaction.
            migrationBuilder.Sql("DELETE FROM confidence.kill_settings WHERE actor = 'system:default' AND reason = 'ADR 0282'");
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION confidence.stamp_kill_setting() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    PERFORM pg_advisory_xact_lock(hashtextextended('confidence.kill_settings', 0));
                    NEW.id := nextval(pg_get_serial_sequence('confidence.kill_settings', 'id'));
                    NEW.changed_at := now();
                    RETURN NEW;
                END
                $fn$
                """);
            migrationBuilder.Sql("ALTER TABLE confidence.kill_settings DROP CONSTRAINT ck_kill_settings_shape");
            migrationBuilder.Sql("ALTER TABLE confidence.kill_settings ADD CONSTRAINT ck_kill_settings_shape CHECK (actor LIKE 'owner:%' AND length(btrim(actor)) > 6 AND length(btrim(reason)) > 0)");
        }
    }
}
