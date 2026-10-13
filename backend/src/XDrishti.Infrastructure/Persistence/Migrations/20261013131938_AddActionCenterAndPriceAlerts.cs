using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddActionCenterAndPriceAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "action_states",
                schema: "portfolio",
                columns: table => new
                {
                    rule = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    state = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    snoozed_until = table.Column<DateOnly>(type: "date", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_action_states", x => new { x.rule, x.key });
                    table.CheckConstraint("ck_action_states_snoozed_until", "(state = 'snoozed') = (snoozed_until IS NOT NULL)");
                    table.CheckConstraint("ck_action_states_state", "state IN ('snoozed', 'done', 'cleared')");
                });

            migrationBuilder.CreateTable(
                name: "price_alerts",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    direction = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    level = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    triggered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    trigger_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_price_alerts", x => x.id);
                    table.CheckConstraint("ck_price_alerts_direction", "direction IN ('at_or_below', 'at_or_above')");
                    table.CheckConstraint("ck_price_alerts_level", "level > 0 AND level <= 10000000");
                    table.CheckConstraint("ck_price_alerts_status", "status IN ('active', 'triggered', 'cancelled')");
                    table.CheckConstraint("ck_price_alerts_triggered", "status <> 'triggered' OR (triggered_at IS NOT NULL AND trigger_price IS NOT NULL)");
                });

            migrationBuilder.CreateIndex(
                name: "ix_price_alerts_status_instrument",
                schema: "portfolio",
                table: "price_alerts",
                columns: new[] { "status", "instrument_id" });

            // Decisions and alerts are kept for good: the runtime role reads, inserts and updates them (a decision is cleared and an alert triggered or cancelled by an update) and can never delete.
            migrationBuilder.GrantTableToAppRole(Schemas.Portfolio, "action_states");
            migrationBuilder.NeverDeleteForAppRole(Schemas.Portfolio, "action_states");
            migrationBuilder.GrantTableToAppRole(Schemas.Portfolio, "price_alerts");
            migrationBuilder.NeverDeleteForAppRole(Schemas.Portfolio, "price_alerts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "action_states",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "price_alerts",
                schema: "portfolio");
        }
    }
}
