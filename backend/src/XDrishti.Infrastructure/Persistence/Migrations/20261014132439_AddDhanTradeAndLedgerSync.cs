using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDhanTradeAndLedgerSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_transactions_source",
                schema: "portfolio",
                table: "transactions");

            migrationBuilder.AddColumn<Guid>(
                name: "run_id",
                schema: "portfolio",
                table: "transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "broker_sync_runs",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    trigger = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    from_date = table.Column<DateOnly>(type: "date", nullable: false),
                    to_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    counts = table.Column<string>(type: "jsonb", nullable: false),
                    started_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_broker_sync_runs", x => x.id);
                    table.CheckConstraint("ck_broker_sync_runs_kind", "kind IN ('trades', 'ledger')");
                    table.CheckConstraint("ck_broker_sync_runs_status", "status IN ('running', 'succeeded', 'failed', 'skipped')");
                    table.CheckConstraint("ck_broker_sync_runs_trigger", "trigger IN ('manual', 'scheduled', 'first')");
                    table.ForeignKey(
                        name: "fk_broker_sync_runs_broker_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "accounts",
                        principalTable: "broker_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "broker_sync_state",
                schema: "portfolio",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    watermark_date = table.Column<DateOnly>(type: "date", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_broker_sync_state", x => new { x.account_id, x.kind });
                    table.CheckConstraint("ck_broker_sync_state_kind", "kind IN ('trades', 'ledger')");
                    table.ForeignKey(
                        name: "fk_broker_sync_state_broker_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "accounts",
                        principalTable: "broker_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ledger_entries",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_date = table.Column<DateOnly>(type: "date", nullable: false),
                    voucher_no = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    narration = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    exchange = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    @class = table.Column<string>(name: "class", type: "character varying(9)", maxLength: 9, nullable: false),
                    running_balance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    external_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_entries", x => x.id);
                    table.CheckConstraint("ck_ledger_entries_class", "class IN ('funds_in', 'funds_out', 'dividend', 'charges', 'other')");
                    table.ForeignKey(
                        name: "fk_ledger_entries_broker_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "accounts",
                        principalTable: "broker_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ledger_entries_broker_sync_runs_run_id",
                        column: x => x.run_id,
                        principalSchema: "portfolio",
                        principalTable: "broker_sync_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_transactions_run",
                schema: "portfolio",
                table: "transactions",
                column: "run_id",
                filter: "run_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_transactions_source",
                schema: "portfolio",
                table: "transactions",
                sql: "source IN ('csv', 'manual', 'broker')");

            migrationBuilder.CreateIndex(
                name: "ix_broker_sync_runs_account_kind_started",
                schema: "portfolio",
                table: "broker_sync_runs",
                columns: new[] { "account_id", "kind", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ux_broker_sync_runs_one_running",
                schema: "portfolio",
                table: "broker_sync_runs",
                column: "account_id",
                unique: true,
                filter: "status = 'running'");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_account_date",
                schema: "portfolio",
                table: "ledger_entries",
                columns: new[] { "account_id", "entry_date" });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_run",
                schema: "portfolio",
                table: "ledger_entries",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "ux_ledger_entries_account_external",
                schema: "portfolio",
                table: "ledger_entries",
                columns: new[] { "account_id", "external_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_transactions_broker_sync_runs_run_id",
                schema: "portfolio",
                table: "transactions",
                column: "run_id",
                principalSchema: "portfolio",
                principalTable: "broker_sync_runs",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Runs, watermarks and ledger entries are kept for good: the runtime role reads, inserts and updates them and can never delete (trades keep the no-DELETE rule of their own migration).
            migrationBuilder.GrantTableToAppRole(Schemas.Portfolio, "broker_sync_runs");
            migrationBuilder.NeverDeleteForAppRole(Schemas.Portfolio, "broker_sync_runs");
            migrationBuilder.GrantTableToAppRole(Schemas.Portfolio, "broker_sync_state");
            migrationBuilder.NeverDeleteForAppRole(Schemas.Portfolio, "broker_sync_state");
            migrationBuilder.GrantTableToAppRole(Schemas.Portfolio, "ledger_entries");
            migrationBuilder.NeverDeleteForAppRole(Schemas.Portfolio, "ledger_entries");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_transactions_broker_sync_runs_run_id",
                schema: "portfolio",
                table: "transactions");

            migrationBuilder.DropTable(
                name: "broker_sync_state",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "ledger_entries",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "broker_sync_runs",
                schema: "portfolio");

            migrationBuilder.DropIndex(
                name: "ix_transactions_run",
                schema: "portfolio",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_transactions_source",
                schema: "portfolio",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "run_id",
                schema: "portfolio",
                table: "transactions");

            // Trades read from the broker would break the old check: they stay, as trades of a file (their identity keeps them from being imported twice). Not reversible in the other direction.
            migrationBuilder.Sql("UPDATE portfolio.transactions SET source = 'csv' WHERE source = 'broker'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_transactions_source",
                schema: "portfolio",
                table: "transactions",
                sql: "source IN ('csv', 'manual')");
        }
    }
}
