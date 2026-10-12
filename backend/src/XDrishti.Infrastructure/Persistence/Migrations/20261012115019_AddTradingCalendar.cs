using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTradingCalendar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "trading_days",
                schema: "market",
                columns: table => new
                {
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    kind = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    session_from = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    session_to = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    source = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    status = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trading_days", x => x.date);
                    table.CheckConstraint("ck_trading_days_kind", "kind IN ('holiday', 'special')");
                    table.CheckConstraint("ck_trading_days_session", "(kind = 'holiday' AND session_from IS NULL AND session_to IS NULL) OR (kind = 'special' AND session_from IS NOT NULL AND session_to IS NOT NULL AND session_from < session_to)");
                    table.CheckConstraint("ck_trading_days_source", "source IN ('manual', 'observed', 'nse')");
                    table.CheckConstraint("ck_trading_days_status", "status IN ('confirmed', 'proposed', 'dismissed')");
                });

            // The calendar is small configuration the owner edits in the app (add, confirm, remove): the runtime role gets plain DML on it, deletes included.
            migrationBuilder.GrantTableToAppRole(Schemas.Market, "trading_days");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "trading_days",
                schema: "market");
        }
    }
}
