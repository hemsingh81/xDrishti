using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReportImports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_transactions_source",
                schema: "portfolio",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "ux_ledger_entries_account_external",
                schema: "portfolio",
                table: "ledger_entries");

            migrationBuilder.AlterColumn<decimal>(
                name: "price",
                schema: "portfolio",
                table: "transactions",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,4)",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AddColumn<Guid>(
                name: "superseded_by",
                schema: "portfolio",
                table: "transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "run_id",
                schema: "portfolio",
                table: "ledger_entries",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "batch_id",
                schema: "portfolio",
                table: "ledger_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "isin",
                schema: "portfolio",
                table: "ledger_entries",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "quantity",
                schema: "portfolio",
                table: "ledger_entries",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source",
                schema: "portfolio",
                table: "ledger_entries",
                type: "character varying(6)",
                maxLength: 6,
                nullable: false,
                defaultValue: "broker");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "voided_at",
                schema: "portfolio",
                table: "ledger_entries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "voided_by",
                schema: "portfolio",
                table: "ledger_entries",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cost_batches",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    parsed = table.Column<int>(type: "integer", nullable: false),
                    imported = table.Column<int>(type: "integer", nullable: false),
                    duplicates = table.Column<int>(type: "integer", nullable: false),
                    bad = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    undone_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cost_batches", x => x.id);
                    table.CheckConstraint("ck_cost_batches_counts", "parsed >= 0 AND imported >= 0 AND duplicates >= 0 AND bad >= 0 AND amount >= 0");
                    table.CheckConstraint("ck_cost_batches_status", "status IN ('imported', 'undone')");
                    table.CheckConstraint("ck_cost_batches_undone", "(status = 'undone') = (undone_at IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_cost_batches_broker_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "accounts",
                        principalTable: "broker_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "security_aliases",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    isin = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    source = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_security_aliases", x => x.id);
                    table.CheckConstraint("ck_security_aliases_source", "source IN ('demat', 'dp', 'choice')");
                    table.ForeignKey(
                        name: "fk_security_aliases_broker_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "accounts",
                        principalTable: "broker_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_transactions_superseded_by",
                schema: "portfolio",
                table: "transactions",
                column: "superseded_by");

            migrationBuilder.AddCheckConstraint(
                name: "ck_transactions_source",
                schema: "portfolio",
                table: "transactions",
                sql: "source IN ('csv', 'manual', 'broker', 'bill')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_transactions_superseded",
                schema: "portfolio",
                table: "transactions",
                sql: "superseded_by IS NULL OR (voided_at IS NOT NULL AND source = 'bill')");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_batch",
                schema: "portfolio",
                table: "ledger_entries",
                column: "batch_id",
                filter: "batch_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_ledger_entries_account_external",
                schema: "portfolio",
                table: "ledger_entries",
                columns: new[] { "account_id", "external_id" },
                unique: true,
                filter: "voided_at IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ledger_entries_origin",
                schema: "portfolio",
                table: "ledger_entries",
                sql: "(source = 'broker') = (run_id IS NOT NULL) AND (source = 'import') = (batch_id IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ledger_entries_quantity",
                schema: "portfolio",
                table: "ledger_entries",
                sql: "quantity IS NULL OR quantity > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ledger_entries_source",
                schema: "portfolio",
                table: "ledger_entries",
                sql: "source IN ('broker', 'import')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ledger_entries_voided",
                schema: "portfolio",
                table: "ledger_entries",
                sql: "(voided_at IS NULL) = (voided_by IS NULL) AND (voided_at IS NULL OR source = 'import')");

            migrationBuilder.CreateIndex(
                name: "ix_cost_batches_account_created",
                schema: "portfolio",
                table: "cost_batches",
                columns: new[] { "account_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_security_aliases_account_isin",
                schema: "portfolio",
                table: "security_aliases",
                columns: new[] { "account_id", "isin" });

            migrationBuilder.CreateIndex(
                name: "ux_security_aliases_account_name_isin",
                schema: "portfolio",
                table: "security_aliases",
                columns: new[] { "account_id", "name_key", "isin" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_ledger_entries_cost_batches_batch_id",
                schema: "portfolio",
                table: "ledger_entries",
                column: "batch_id",
                principalSchema: "portfolio",
                principalTable: "cost_batches",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_transactions_broker_sync_runs_superseded_by",
                schema: "portfolio",
                table: "transactions",
                column: "superseded_by",
                principalSchema: "portfolio",
                principalTable: "broker_sync_runs",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Costs imported from a depository-charges sheet are voided by undoing their batch, never deleted: the runtime role keeps its no-DELETE rule on the ledger. The batches and the learned names are kept for good too; the learned names are immutable.
            migrationBuilder.GrantTableToAppRole(Schemas.Portfolio, "cost_batches");
            migrationBuilder.NeverDeleteForAppRole(Schemas.Portfolio, "cost_batches");
            migrationBuilder.GrantTableToAppRole(Schemas.Portfolio, "security_aliases");
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Portfolio, "security_aliases");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rows that have no place in the old shape: bill-level trades of imported Dhan reports (their source is not one the old table allows) and costs imported from a depository-charges sheet (they have no run). Lots that were rebuilt from such trades keep their shape.
            migrationBuilder.Sql("DELETE FROM portfolio.transactions WHERE source = 'bill';");
            migrationBuilder.Sql("DELETE FROM portfolio.ledger_entries WHERE source = 'import';");

            migrationBuilder.DropForeignKey(
                name: "fk_ledger_entries_cost_batches_batch_id",
                schema: "portfolio",
                table: "ledger_entries");

            migrationBuilder.DropForeignKey(
                name: "fk_transactions_broker_sync_runs_superseded_by",
                schema: "portfolio",
                table: "transactions");

            migrationBuilder.DropTable(
                name: "cost_batches",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "security_aliases",
                schema: "portfolio");

            migrationBuilder.DropIndex(
                name: "ix_transactions_superseded_by",
                schema: "portfolio",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_transactions_source",
                schema: "portfolio",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_transactions_superseded",
                schema: "portfolio",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "ix_ledger_entries_batch",
                schema: "portfolio",
                table: "ledger_entries");

            migrationBuilder.DropIndex(
                name: "ux_ledger_entries_account_external",
                schema: "portfolio",
                table: "ledger_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ledger_entries_origin",
                schema: "portfolio",
                table: "ledger_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ledger_entries_quantity",
                schema: "portfolio",
                table: "ledger_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ledger_entries_source",
                schema: "portfolio",
                table: "ledger_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ledger_entries_voided",
                schema: "portfolio",
                table: "ledger_entries");

            migrationBuilder.DropColumn(
                name: "superseded_by",
                schema: "portfolio",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "batch_id",
                schema: "portfolio",
                table: "ledger_entries");

            migrationBuilder.DropColumn(
                name: "isin",
                schema: "portfolio",
                table: "ledger_entries");

            migrationBuilder.DropColumn(
                name: "quantity",
                schema: "portfolio",
                table: "ledger_entries");

            migrationBuilder.DropColumn(
                name: "source",
                schema: "portfolio",
                table: "ledger_entries");

            migrationBuilder.DropColumn(
                name: "voided_at",
                schema: "portfolio",
                table: "ledger_entries");

            migrationBuilder.DropColumn(
                name: "voided_by",
                schema: "portfolio",
                table: "ledger_entries");

            migrationBuilder.AlterColumn<decimal>(
                name: "price",
                schema: "portfolio",
                table: "transactions",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,6)",
                oldPrecision: 18,
                oldScale: 6);

            migrationBuilder.AlterColumn<Guid>(
                name: "run_id",
                schema: "portfolio",
                table: "ledger_entries",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_transactions_source",
                schema: "portfolio",
                table: "transactions",
                sql: "source IN ('csv', 'manual', 'broker')");

            migrationBuilder.CreateIndex(
                name: "ux_ledger_entries_account_external",
                schema: "portfolio",
                table: "ledger_entries",
                columns: new[] { "account_id", "external_id" },
                unique: true);
        }
    }
}
