using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHoldingQuotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "last_price_source",
                schema: "portfolio",
                table: "holdings",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "sync");

            migrationBuilder.AddColumn<decimal>(
                name: "previous_close",
                schema: "portfolio",
                table: "holdings",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_holdings_last_price_source",
                schema: "portfolio",
                table: "holdings",
                sql: "last_price_source IN ('sync', 'quote')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_holdings_last_price_source",
                schema: "portfolio",
                table: "holdings");

            migrationBuilder.DropColumn(
                name: "last_price_source",
                schema: "portfolio",
                table: "holdings");

            migrationBuilder.DropColumn(
                name: "previous_close",
                schema: "portfolio",
                table: "holdings");
        }
    }
}
