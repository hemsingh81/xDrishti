using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The ledger of the recovery passes (spec AU-02, ADR 0240): <c>platform.recovery_passes</c> and <c>platform.recovery_items</c>, unique on service and occurrence so a repeated pass is idempotent; the runtime role reads, inserts and updates
    /// and never deletes. Raw SQL: no EF entity maps the two tables. <c>trading.plan_runs.recovered</c> marks a nightly run made up by a pass (never counted as unattended by the phase check).
    /// </summary>
    public partial class AddRecoveryLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "recovered",
                schema: "trading",
                table: "plan_runs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""
                CREATE TABLE platform.recovery_passes (
                    id uuid NOT NULL,
                    trigger varchar(20) NOT NULL,
                    started_at timestamptz NOT NULL,
                    finished_at timestamptz NULL,
                    status varchar(16) NOT NULL,
                    CONSTRAINT pk_recovery_passes PRIMARY KEY (id),
                    CONSTRAINT ck_recovery_passes_trigger CHECK (trigger IN ('startup', 'wake', 'broker_reconnect', 'resume')),
                    CONSTRAINT ck_recovery_passes_status CHECK (status IN ('running', 'succeeded', 'incomplete', 'failed', 'interrupted') AND (status = 'running') = (finished_at IS NULL))
                );
                CREATE TABLE platform.recovery_items (
                    id uuid NOT NULL,
                    pass_id uuid NOT NULL,
                    service_id varchar(60) NOT NULL,
                    occurrence varchar(40) NOT NULL,
                    session_from date NOT NULL,
                    session_to date NOT NULL,
                    state varchar(12) NOT NULL,
                    reason varchar(400) NULL,
                    attempts smallint NOT NULL DEFAULT 0,
                    run_id uuid NULL,
                    CONSTRAINT pk_recovery_items PRIMARY KEY (id),
                    CONSTRAINT fk_recovery_items_passes FOREIGN KEY (pass_id) REFERENCES platform.recovery_passes (id),
                    CONSTRAINT uq_recovery_items_service_occurrence UNIQUE (service_id, occurrence),
                    CONSTRAINT ck_recovery_items_state CHECK (state IN ('running', 'succeeded', 'failed', 'blocked', 'waiting') AND attempts >= 0 AND session_from <= session_to)
                );
                CREATE INDEX ix_recovery_items_pass_id ON platform.recovery_items (pass_id);
                """);
            migrationBuilder.GrantTableToAppRole(Schemas.Platform, "recovery_passes");
            migrationBuilder.GrantTableToAppRole(Schemas.Platform, "recovery_items");
            migrationBuilder.NeverDeleteForAppRole(Schemas.Platform, "recovery_passes");
            migrationBuilder.NeverDeleteForAppRole(Schemas.Platform, "recovery_items");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS platform.recovery_items; DROP TABLE IF EXISTS platform.recovery_passes;");
            migrationBuilder.DropColumn(
                name: "recovered",
                schema: "trading",
                table: "plan_runs");
        }
    }
}
