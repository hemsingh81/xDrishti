using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropMarketContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ADR 0291: the NIFTY 50 / INDIA VIX market context is dropped (the table never held a row), with the VIX side table of the CSV inbox.
            migrationBuilder.DropTable(
                name: "context_daily",
                schema: "market");

            migrationBuilder.DropTable(
                name: "context_vix_csv",
                schema: "market");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not reversible: the dropped tables held the NIFTY 50 / INDIA VIX context (0 rows when dropped, ADR 0291). AddContextDaily, AddContextBreadth and AddContextVixCsv create them if ever needed again.
        }
    }
}
