using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ticket_alerts",
                schema: "trading",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    plan_id = table.Column<long>(type: "bigint", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    session = table.Column<DateOnly>(type: "date", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    level = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    stop_anchor = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    tick_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ticket_alerts", x => x.id);
                    table.CheckConstraint("ck_ticket_alerts_shape", "kind IN ('stop_crossed', 'target_crossed') AND level > 0 AND price > 0 AND (stop_anchor IS NULL OR stop_anchor IN ('level', 'close_of_d'))");
                    table.ForeignKey(
                        name: "fk_ticket_alerts_plan_items_item_id",
                        column: x => x.item_id,
                        principalSchema: "trading",
                        principalTable: "plan_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ticket_alerts_plans_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "trading",
                        principalTable: "plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ticket_notice_acks",
                schema: "trading",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    notice_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    notice_id = table.Column<long>(type: "bigint", nullable: true),
                    item_id = table.Column<long>(type: "bigint", nullable: true),
                    action = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ticket_notice_acks", x => x.id);
                    table.CheckConstraint("ck_ticket_notice_acks_shape", "notice_kind IN ('crossing', 'prompt', 'prompt_ambiguous', 'ticket') AND action IN ('snooze', 'dismiss', 'mute') AND actor LIKE 'owner:%' AND length(btrim(actor)) > 6 AND ((action = 'snooze' AND notice_kind IN ('crossing', 'prompt', 'prompt_ambiguous') AND notice_id IS NOT NULL AND until IS NOT NULL AND item_id IS NULL) OR (action = 'dismiss' AND notice_kind IN ('crossing', 'prompt', 'prompt_ambiguous') AND notice_id IS NOT NULL AND until IS NULL AND item_id IS NULL) OR (action = 'mute' AND notice_kind = 'ticket' AND item_id IS NOT NULL AND notice_id IS NULL AND until IS NULL))");
                    table.ForeignKey(
                        name: "fk_ticket_notice_acks_plan_items_item_id",
                        column: x => x.item_id,
                        principalSchema: "trading",
                        principalTable: "plan_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ticket_prompts",
                schema: "trading",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    plan_id = table.Column<long>(type: "bigint", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ticket_prompts", x => x.id);
                    table.ForeignKey(
                        name: "fk_ticket_prompts_plan_items_item_id",
                        column: x => x.item_id,
                        principalSchema: "trading",
                        principalTable: "plan_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ticket_prompts_plans_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "trading",
                        principalTable: "plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ticket_alerts_plan_id",
                schema: "trading",
                table: "ticket_alerts",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_alerts_session",
                schema: "trading",
                table: "ticket_alerts",
                column: "session");

            migrationBuilder.CreateIndex(
                name: "uq_ticket_alerts_item_kind",
                schema: "trading",
                table: "ticket_alerts",
                columns: new[] { "item_id", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ticket_notice_acks_item_id",
                schema: "trading",
                table: "ticket_notice_acks",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_notice_acks_notice",
                schema: "trading",
                table: "ticket_notice_acks",
                columns: new[] { "notice_kind", "notice_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ticket_prompts_plan_id",
                schema: "trading",
                table: "ticket_prompts",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_prompts_session",
                schema: "trading",
                table: "ticket_prompts",
                column: "session");

            migrationBuilder.CreateIndex(
                name: "uq_ticket_prompts_item_account",
                schema: "trading",
                table: "ticket_prompts",
                columns: new[] { "item_id", "account_id" },
                unique: true);

            // A crossing or prompt belongs to a real item of its own plan; a prompt to a real paper allocation (composite keys onto the unique pairs of plan_items and plan_item_allocations).
            migrationBuilder.Sql("ALTER TABLE trading.ticket_alerts ADD CONSTRAINT fk_ticket_alerts_item_in_plan FOREIGN KEY (plan_id, item_id) REFERENCES trading.plan_items (plan_id, id) ON DELETE RESTRICT");
            migrationBuilder.Sql("ALTER TABLE trading.ticket_prompts ADD CONSTRAINT fk_ticket_prompts_item_in_plan FOREIGN KEY (plan_id, item_id) REFERENCES trading.plan_items (plan_id, id) ON DELETE RESTRICT");
            migrationBuilder.Sql("ALTER TABLE trading.ticket_prompts ADD CONSTRAINT fk_ticket_prompts_allocation FOREIGN KEY (item_id, account_id) REFERENCES trading.plan_item_allocations (item_id, account_id) ON DELETE RESTRICT");

            // Insert-only for the runtime role (ADR 0115): granted per table, never by re-granting the schema.
            foreach (var table in new[] { "ticket_alerts", "ticket_prompts", "ticket_notice_acks" })
            {
                migrationBuilder.GrantTableToAppRole(Schemas.Trading, table);
                migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Trading, table);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ticket_alerts",
                schema: "trading");

            migrationBuilder.DropTable(
                name: "ticket_notice_acks",
                schema: "trading");

            migrationBuilder.DropTable(
                name: "ticket_prompts",
                schema: "trading");
        }
    }
}
