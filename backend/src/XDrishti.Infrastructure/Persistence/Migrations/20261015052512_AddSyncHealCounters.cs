using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSyncHealCounters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_broker_sync_state_kind",
                schema: "portfolio",
                table: "broker_sync_state");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "watermark_date",
                schema: "portfolio",
                table: "broker_sync_state",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "date");

            migrationBuilder.AlterColumn<string>(
                name: "kind",
                schema: "portfolio",
                table: "broker_sync_state",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(6)",
                oldMaxLength: 6);

            migrationBuilder.AddColumn<int>(
                name: "attempts",
                schema: "portfolio",
                table: "broker_sync_state",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "consecutive_failed_days",
                schema: "portfolio",
                table: "broker_sync_state",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "deepen_exhausted",
                schema: "portfolio",
                table: "broker_sync_state",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "deepened_on",
                schema: "portfolio",
                table: "broker_sync_state",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "deepened_to",
                schema: "portfolio",
                table: "broker_sync_state",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_failed_at",
                schema: "portfolio",
                table: "broker_sync_state",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_failure_reason",
                schema: "portfolio",
                table: "broker_sync_state",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "mapper_version",
                schema: "portfolio",
                table: "broker_sync_state",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "attention_items",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    severity = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    actions = table.Column<string>(type: "jsonb", nullable: false),
                    since = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attention_items", x => x.id);
                    table.CheckConstraint("ck_attention_items_severity", "severity IN ('error', 'warning', 'info')");
                });

            migrationBuilder.CreateTable(
                name: "broker_sync_windows",
                schema: "portfolio",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    from_date = table.Column<DateOnly>(type: "date", nullable: false),
                    to_date = table.Column<DateOnly>(type: "date", nullable: false),
                    dropped = table.Column<int>(type: "integer", nullable: false),
                    needs_reread = table.Column<bool>(type: "boolean", nullable: false),
                    noted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_broker_sync_windows", x => new { x.account_id, x.kind, x.from_date });
                    table.CheckConstraint("ck_broker_sync_windows_kind", "kind IN ('trades', 'ledger')");
                    table.ForeignKey(
                        name: "fk_broker_sync_windows_broker_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "accounts",
                        principalTable: "broker_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_broker_sync_state_kind",
                schema: "portfolio",
                table: "broker_sync_state",
                sql: "kind IN ('holdings', 'trades', 'ledger', 'quotes')");

            migrationBuilder.CreateIndex(
                name: "ix_attention_items_resolved",
                schema: "portfolio",
                table: "attention_items",
                column: "resolved_at");

            migrationBuilder.CreateIndex(
                name: "ix_broker_sync_windows_reread",
                schema: "portfolio",
                table: "broker_sync_windows",
                columns: new[] { "account_id", "kind", "from_date" },
                filter: "needs_reread");

            // The memory of the attention items and the noted windows are kept for good: the runtime role reads, inserts and updates them and can never delete (the state table keeps the no-DELETE rule of its own migration).
            migrationBuilder.GrantTableToAppRole(Schemas.Portfolio, "attention_items");
            migrationBuilder.NeverDeleteForAppRole(Schemas.Portfolio, "attention_items");
            migrationBuilder.GrantTableToAppRole(Schemas.Portfolio, "broker_sync_windows");
            migrationBuilder.NeverDeleteForAppRole(Schemas.Portfolio, "broker_sync_windows");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attention_items",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "broker_sync_windows",
                schema: "portfolio");

            migrationBuilder.DropCheckConstraint(
                name: "ck_broker_sync_state_kind",
                schema: "portfolio",
                table: "broker_sync_state");

            // Rows that only hold health figures (a kind without a watermark: holdings, quotes, or a trade or ledger read that failed before its first window) have no place in the old shape.
            migrationBuilder.Sql("DELETE FROM portfolio.broker_sync_state WHERE watermark_date IS NULL OR kind NOT IN ('trades', 'ledger');");

            migrationBuilder.DropColumn(
                name: "attempts",
                schema: "portfolio",
                table: "broker_sync_state");

            migrationBuilder.DropColumn(
                name: "consecutive_failed_days",
                schema: "portfolio",
                table: "broker_sync_state");

            migrationBuilder.DropColumn(
                name: "deepen_exhausted",
                schema: "portfolio",
                table: "broker_sync_state");

            migrationBuilder.DropColumn(
                name: "deepened_on",
                schema: "portfolio",
                table: "broker_sync_state");

            migrationBuilder.DropColumn(
                name: "deepened_to",
                schema: "portfolio",
                table: "broker_sync_state");

            migrationBuilder.DropColumn(
                name: "last_failed_at",
                schema: "portfolio",
                table: "broker_sync_state");

            migrationBuilder.DropColumn(
                name: "last_failure_reason",
                schema: "portfolio",
                table: "broker_sync_state");

            migrationBuilder.DropColumn(
                name: "mapper_version",
                schema: "portfolio",
                table: "broker_sync_state");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "watermark_date",
                schema: "portfolio",
                table: "broker_sync_state",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1),
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "kind",
                schema: "portfolio",
                table: "broker_sync_state",
                type: "character varying(6)",
                maxLength: 6,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(8)",
                oldMaxLength: 8);

            migrationBuilder.AddCheckConstraint(
                name: "ck_broker_sync_state_kind",
                schema: "portfolio",
                table: "broker_sync_state",
                sql: "kind IN ('trades', 'ledger')");
        }
    }
}
