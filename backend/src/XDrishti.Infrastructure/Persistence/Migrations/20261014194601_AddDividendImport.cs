using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDividendImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_dividends_source",
                schema: "portfolio",
                table: "dividends");

            migrationBuilder.AddColumn<Guid>(
                name: "batch_id",
                schema: "portfolio",
                table: "dividends",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "external_id",
                schema: "portfolio",
                table: "dividends",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "quantity",
                schema: "portfolio",
                table: "dividends",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "scrip_name",
                schema: "portfolio",
                table: "dividends",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "dividend_aliases",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    holding_id = table.Column<Guid>(type: "uuid", nullable: true),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dividend_aliases", x => x.id);
                    table.CheckConstraint("ck_dividend_aliases_target", "holding_id IS NULL OR instrument_id IS NULL");
                    table.ForeignKey(
                        name: "fk_dividend_aliases_broker_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "accounts",
                        principalTable: "broker_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dividend_aliases_holdings_holding_id",
                        column: x => x.holding_id,
                        principalSchema: "portfolio",
                        principalTable: "holdings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dividend_batches",
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
                    undone_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dividend_batches", x => x.id);
                    table.CheckConstraint("ck_dividend_batches_counts", "parsed >= 0 AND imported >= 0 AND duplicates >= 0 AND bad >= 0 AND amount >= 0");
                    table.CheckConstraint("ck_dividend_batches_status", "status IN ('imported', 'undone')");
                    table.CheckConstraint("ck_dividend_batches_undone", "(status = 'undone') = (undone_at IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_dividend_batches_broker_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "accounts",
                        principalTable: "broker_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_dividends_batch",
                schema: "portfolio",
                table: "dividends",
                column: "batch_id",
                filter: "batch_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_dividends_account_external_active",
                schema: "portfolio",
                table: "dividends",
                columns: new[] { "account_id", "external_id" },
                unique: true,
                filter: "voided_at IS NULL AND external_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_dividends_file",
                schema: "portfolio",
                table: "dividends",
                sql: "(source = 'csv') = (external_id IS NOT NULL AND batch_id IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_dividends_quantity",
                schema: "portfolio",
                table: "dividends",
                sql: "quantity IS NULL OR quantity > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_dividends_source",
                schema: "portfolio",
                table: "dividends",
                sql: "source IN ('manual', 'csv')");

            migrationBuilder.CreateIndex(
                name: "ix_dividend_aliases_holding",
                schema: "portfolio",
                table: "dividend_aliases",
                column: "holding_id",
                filter: "holding_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_dividend_aliases_account_name",
                schema: "portfolio",
                table: "dividend_aliases",
                columns: new[] { "account_id", "name_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_dividend_batches_account_created",
                schema: "portfolio",
                table: "dividend_batches",
                columns: new[] { "account_id", "created_at" });

            migrationBuilder.AddForeignKey(
                name: "fk_dividends_dividend_batches_batch_id",
                schema: "portfolio",
                table: "dividends",
                column: "batch_id",
                principalSchema: "portfolio",
                principalTable: "dividend_batches",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Batches and remembered names are kept for good: the runtime role reads, inserts and updates them (undo marks a batch, a remembered name is pointed elsewhere by an update) and can never delete.
            // The dividends table keeps the grants and the missing DELETE of migration AddDividends.
            migrationBuilder.GrantTableToAppRole(Schemas.Portfolio, "dividend_batches");
            migrationBuilder.NeverDeleteForAppRole(Schemas.Portfolio, "dividend_batches");
            migrationBuilder.GrantTableToAppRole(Schemas.Portfolio, "dividend_aliases");
            migrationBuilder.NeverDeleteForAppRole(Schemas.Portfolio, "dividend_aliases");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The dividends read from files cannot stay: their batches, identities and file columns go. This deletes every dividend with source 'csv', the voided ones included, so the history of imports and undos is lost
            // (and the voided dividends with it); dividends typed in are kept. Down is for a development database, never for one with imports the owner wants to keep.
            migrationBuilder.Sql("DELETE FROM portfolio.dividends WHERE source = 'csv'");

            migrationBuilder.DropForeignKey(
                name: "fk_dividends_dividend_batches_batch_id",
                schema: "portfolio",
                table: "dividends");

            migrationBuilder.DropTable(
                name: "dividend_aliases",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "dividend_batches",
                schema: "portfolio");

            migrationBuilder.DropIndex(
                name: "ix_dividends_batch",
                schema: "portfolio",
                table: "dividends");

            migrationBuilder.DropIndex(
                name: "ux_dividends_account_external_active",
                schema: "portfolio",
                table: "dividends");

            migrationBuilder.DropCheckConstraint(
                name: "ck_dividends_file",
                schema: "portfolio",
                table: "dividends");

            migrationBuilder.DropCheckConstraint(
                name: "ck_dividends_quantity",
                schema: "portfolio",
                table: "dividends");

            migrationBuilder.DropCheckConstraint(
                name: "ck_dividends_source",
                schema: "portfolio",
                table: "dividends");

            migrationBuilder.DropColumn(
                name: "batch_id",
                schema: "portfolio",
                table: "dividends");

            migrationBuilder.DropColumn(
                name: "external_id",
                schema: "portfolio",
                table: "dividends");

            migrationBuilder.DropColumn(
                name: "quantity",
                schema: "portfolio",
                table: "dividends");

            migrationBuilder.DropColumn(
                name: "scrip_name",
                schema: "portfolio",
                table: "dividends");

            migrationBuilder.AddCheckConstraint(
                name: "ck_dividends_source",
                schema: "portfolio",
                table: "dividends",
                sql: "source IN ('manual')");
        }
    }
}
