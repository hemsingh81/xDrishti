using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContextDaily : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                    written_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_context_daily", x => x.date);
                    table.CheckConstraint("ck_context_daily_bars", "index_bars > 0 AND (vix_bars IS NULL OR vix_bars > 0)");
                    table.CheckConstraint("ck_context_daily_index", "index_low > 0 AND index_high >= index_low AND index_close >= index_low AND index_close <= index_high");
                    table.CheckConstraint("ck_context_daily_vix", "vix_close IS NULL OR vix_close > 0");
                });

            // Insert-only for the runtime role, granted on this table alone (a schema-wide grant would give back UPDATE and DELETE). P3-01e adds breadth columns and a column-level UPDATE grant, never a table-level one.
            migrationBuilder.GrantTableToAppRole(Schemas.Market, "context_daily");
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Market, "context_daily");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "context_daily",
                schema: "market");
        }
    }
}
