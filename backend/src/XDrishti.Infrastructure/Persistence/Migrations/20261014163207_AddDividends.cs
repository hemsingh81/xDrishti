using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDividends : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dividends",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    holding_id = table.Column<Guid>(type: "uuid", nullable: true),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: true),
                    isin = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    trading_symbol = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    pay_date = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    per_share = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    source = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    voided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    voided_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dividends", x => x.id);
                    table.CheckConstraint("ck_dividends_amount", "amount > 0");
                    table.CheckConstraint("ck_dividends_per_share", "per_share IS NULL OR per_share > 0");
                    table.CheckConstraint("ck_dividends_source", "source IN ('manual')");
                    table.CheckConstraint("ck_dividends_voided", "(voided_at IS NULL) = (voided_by IS NULL)");
                    table.ForeignKey(
                        name: "fk_dividends_broker_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "accounts",
                        principalTable: "broker_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dividends_holdings_holding_id",
                        column: x => x.holding_id,
                        principalSchema: "portfolio",
                        principalTable: "holdings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dividend_suggestions",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    dividend_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dividend_suggestions", x => x.id);
                    table.CheckConstraint("ck_dividend_suggestions_dividend", "(state = 'accepted') = (dividend_id IS NOT NULL)");
                    table.CheckConstraint("ck_dividend_suggestions_state", "state IN ('accepted', 'dismissed')");
                    table.ForeignKey(
                        name: "fk_dividend_suggestions_broker_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "accounts",
                        principalTable: "broker_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dividend_suggestions_dividends_dividend_id",
                        column: x => x.dividend_id,
                        principalSchema: "portfolio",
                        principalTable: "dividends",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dividend_suggestions_ledger_entries_id",
                        column: x => x.id,
                        principalSchema: "portfolio",
                        principalTable: "ledger_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_dividend_suggestions_account",
                schema: "portfolio",
                table: "dividend_suggestions",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_dividend_suggestions_dividend_id",
                schema: "portfolio",
                table: "dividend_suggestions",
                column: "dividend_id");

            migrationBuilder.CreateIndex(
                name: "ix_dividends_account_pay_date",
                schema: "portfolio",
                table: "dividends",
                columns: new[] { "account_id", "pay_date" });

            migrationBuilder.CreateIndex(
                name: "ix_dividends_holding_pay_date",
                schema: "portfolio",
                table: "dividends",
                columns: new[] { "holding_id", "pay_date" },
                filter: "holding_id IS NOT NULL");

            // Dividends and the owner's decisions about suggested ones are kept for good: the runtime role reads, inserts and updates them (a dividend is voided by an update) and can never delete.
            migrationBuilder.GrantTableToAppRole(Schemas.Portfolio, "dividends");
            migrationBuilder.NeverDeleteForAppRole(Schemas.Portfolio, "dividends");
            migrationBuilder.GrantTableToAppRole(Schemas.Portfolio, "dividend_suggestions");
            migrationBuilder.NeverDeleteForAppRole(Schemas.Portfolio, "dividend_suggestions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dividend_suggestions",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "dividends",
                schema: "portfolio");
        }
    }
}
