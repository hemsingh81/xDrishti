using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContextVixCsv : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            // Insert-only for the runtime role, granted on this table alone (P3-01d fallback D, ADR 0290).
            migrationBuilder.GrantTableToAppRole(Schemas.Market, "context_vix_csv");
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Market, "context_vix_csv");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "context_vix_csv",
                schema: "market");
        }
    }
}
