using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTradeTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_holding_lots_source",
                schema: "portfolio",
                table: "holding_lots");

            migrationBuilder.CreateTable(
                name: "trade_batches",
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
                    skipped = table.Column<int>(type: "integer", nullable: false),
                    bad = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    undone_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trade_batches", x => x.id);
                    table.CheckConstraint("ck_trade_batches_counts", "parsed >= 0 AND imported >= 0 AND duplicates >= 0 AND skipped >= 0 AND bad >= 0");
                    table.CheckConstraint("ck_trade_batches_status", "status IN ('imported', 'undone')");
                    table.CheckConstraint("ck_trade_batches_undone", "(status = 'undone') = (undone_at IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_trade_batches_broker_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "accounts",
                        principalTable: "broker_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "transactions",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: true),
                    isin = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    trading_symbol = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    exchange = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    side = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    trade_date = table.Column<DateOnly>(type: "date", nullable: false),
                    trade_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    qty = table.Column<long>(type: "bigint", nullable: false),
                    price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    charges = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    source = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    seq = table.Column<int>(type: "integer", nullable: false),
                    external_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    voided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    voided_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transactions", x => x.id);
                    table.CheckConstraint("ck_transactions_charges", "charges >= 0");
                    table.CheckConstraint("ck_transactions_price", "price > 0");
                    table.CheckConstraint("ck_transactions_qty", "qty > 0");
                    table.CheckConstraint("ck_transactions_side", "side IN ('buy', 'sell')");
                    table.CheckConstraint("ck_transactions_source", "source IN ('csv', 'manual')");
                    table.CheckConstraint("ck_transactions_voided", "(voided_at IS NULL) = (voided_by IS NULL)");
                    table.ForeignKey(
                        name: "fk_transactions_broker_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "accounts",
                        principalTable: "broker_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_transactions_trade_batches_batch_id",
                        column: x => x.batch_id,
                        principalSchema: "portfolio",
                        principalTable: "trade_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_holding_lots_source",
                schema: "portfolio",
                table: "holding_lots",
                sql: "source IN ('import', 'added', 'manual', 'trade')");

            migrationBuilder.CreateIndex(
                name: "ix_trade_batches_account_created",
                schema: "portfolio",
                table: "trade_batches",
                columns: new[] { "account_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_transactions_account_isin",
                schema: "portfolio",
                table: "transactions",
                columns: new[] { "account_id", "isin" });

            migrationBuilder.CreateIndex(
                name: "ix_transactions_account_symbol",
                schema: "portfolio",
                table: "transactions",
                columns: new[] { "account_id", "trading_symbol" });

            migrationBuilder.CreateIndex(
                name: "ix_transactions_batch",
                schema: "portfolio",
                table: "transactions",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "ux_transactions_account_external_active",
                schema: "portfolio",
                table: "transactions",
                columns: new[] { "account_id", "external_id" },
                unique: true,
                filter: "voided_at IS NULL");

            // Trades and import batches are kept for good (a mistake is voided, an import undone): the runtime role reads, inserts and updates them and can never delete. Lots keep their DELETE (a rebuild replaces them).
            migrationBuilder.GrantTableToAppRole(Schemas.Portfolio, "trade_batches");
            migrationBuilder.NeverDeleteForAppRole(Schemas.Portfolio, "trade_batches");
            migrationBuilder.GrantTableToAppRole(Schemas.Portfolio, "transactions");
            migrationBuilder.NeverDeleteForAppRole(Schemas.Portfolio, "transactions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "transactions",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "trade_batches",
                schema: "portfolio");

            migrationBuilder.DropCheckConstraint(
                name: "ck_holding_lots_source",
                schema: "portfolio",
                table: "holding_lots");

            // Lots rebuilt from trades would break the old check: they become imported lots (their dates and costs stay; the trades they came from go with the dropped tables). Not reversible in the other direction.
            migrationBuilder.Sql("UPDATE portfolio.holding_lots SET source = 'import' WHERE source = 'trade'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_holding_lots_source",
                schema: "portfolio",
                table: "holding_lots",
                sql: "source IN ('import', 'added', 'manual')");
        }
    }
}
