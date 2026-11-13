using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// AM-01a (ADR 0282), data only. (1) <c>confidence.kill_settings</c> may carry the seed row of the system: the shape check now also allows actor <c>system:default</c> with reason <c>ADR 0282</c> and the switch ON (nothing else is added for the system), and a
    /// seed row <c>auto = true</c> is inserted only into an empty table, so the owner's own rows (either direction) always win and a lost table still reads OFF. (2) The six services that start enabled (ml-monitor, readiness-snapshot, decay-monitor, learned-settings,
    /// paper-capture, paper-review) are enabled once where the only stored row is the system's first-start seed (the owner never changed it); a row the owner saved, paused or not, is never touched. A service with no row runs on its definition, which is now enabled.
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
                INSERT INTO confidence.kill_settings (auto, actor, reason)
                SELECT true, 'system:default', 'ADR 0282'
                WHERE NOT EXISTS (SELECT 1 FROM confidence.kill_settings)
                """);
            migrationBuilder.Sql("""
                UPDATE platform.service_schedules
                SET enabled = true, updated_by = 'system:default', updated_at = now()
                WHERE service_id IN ('ml-monitor', 'readiness-snapshot', 'decay-monitor', 'learned-settings', 'paper-capture', 'paper-review')
                  AND updated_by = 'system:first-start' AND NOT enabled
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM confidence.kill_settings WHERE actor = 'system:default' AND reason = 'ADR 0282'");
            migrationBuilder.Sql("""
                UPDATE platform.service_schedules SET enabled = false, updated_by = 'system:first-start'
                WHERE service_id IN ('ml-monitor', 'readiness-snapshot', 'decay-monitor', 'learned-settings', 'paper-capture', 'paper-review') AND updated_by = 'system:default'
                """);
            migrationBuilder.Sql("ALTER TABLE confidence.kill_settings DROP CONSTRAINT ck_kill_settings_shape");
            migrationBuilder.Sql("ALTER TABLE confidence.kill_settings ADD CONSTRAINT ck_kill_settings_shape CHECK (actor LIKE 'owner:%' AND length(btrim(actor)) > 6 AND length(btrim(reason)) > 0)");
        }
    }
}
