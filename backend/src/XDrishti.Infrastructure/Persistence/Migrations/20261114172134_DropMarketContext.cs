using System;
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
            // ADR 0292: the NIFTY 50 / INDIA VIX market context is dropped (the table never held a row), with the VIX side table of the CSV inbox.
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
            // Recreates the two empty tables in their last shape (AddContextDaily + AddContextBreadth, AddContextVixCsv) with their grants, so the migration chain stays reversible (the earlier migrations' Down steps drop these tables). The rows are not restored: there were none (ADR 0292).
            migrationBuilder.CreateTable(
                name: "context_daily",
                schema: "market",
                columns: table => new
                {
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    index_instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    index_high = table.Column<double>(type: "double precision", nullable: false),
                    index_low = table.Column<double>(type: "double precision", nullable: false),
                    index_close = table.Column<double>(type: "double precision", nullable: false),
                    vix_close = table.Column<double>(type: "double precision", nullable: true),
                    index_bars = table.Column<short>(type: "smallint", nullable: false),
                    vix_bars = table.Column<short>(type: "smallint", nullable: true),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    written_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    breadth = table.Column<double>(type: "double precision", nullable: true),
                    breadth_computed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    breadth_excluded = table.Column<short>(type: "smallint", nullable: true),
                    breadth_state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    breadth_universe = table.Column<short>(type: "smallint", nullable: true),
                    breadth_voters = table.Column<short>(type: "smallint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_context_daily", x => x.date);
                    table.CheckConstraint("ck_context_daily_bars", "index_bars > 0 AND (vix_bars IS NULL OR vix_bars > 0)");
                    table.CheckConstraint("ck_context_daily_index", "index_low > 0 AND index_high >= index_low AND index_close >= index_low AND index_close <= index_high");
                    table.CheckConstraint("ck_context_daily_vix", "vix_close IS NULL OR vix_close > 0");
                    table.CheckConstraint("ck_context_daily_breadth", "breadth IS NULL OR (breadth >= 0 AND breadth <= 100)");
                    table.CheckConstraint("ck_context_daily_breadth_state", "breadth_state IS NULL OR breadth_state IN ('value', 'insufficient')");
                });
            migrationBuilder.GrantTableToAppRole(Schemas.Market, "context_daily");
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Market, "context_daily");
            migrationBuilder.GrantColumnUpdateToAppRole("market", "context_daily", "breadth", "breadth_voters", "breadth_universe", "breadth_excluded", "breadth_state", "breadth_computed_at");

            migrationBuilder.CreateTable(
                name: "context_vix_csv",
                schema: "market",
                columns: table => new
                {
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    vix_close = table.Column<double>(type: "double precision", nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    written_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_context_vix_csv", x => x.date);
                    table.CheckConstraint("ck_context_vix_csv_close", "vix_close > 0");
                });
            migrationBuilder.GrantTableToAppRole(Schemas.Market, "context_vix_csv");
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Market, "context_vix_csv");
        }
    }
}
