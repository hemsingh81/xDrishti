using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackedInstruments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tracked_instruments",
                schema: "market",
                columns: table => new
                {
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    active_since = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deactivated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    data_from = table.Column<DateOnly>(type: "date", nullable: true),
                    data_to = table.Column<DateOnly>(type: "date", nullable: true),
                    last_update = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    backfill_requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    tracked_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tracked_instruments", x => x.instrument_id);
                    table.CheckConstraint("ck_tracked_instruments_data_range", "data_from IS NULL OR data_to IS NULL OR data_from <= data_to");
                    table.CheckConstraint("ck_tracked_instruments_state", "(active AND deactivated_at IS NULL) OR (NOT active AND deactivated_at IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_tracked_instruments_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalSchema: "market",
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tracked_instruments_active",
                schema: "market",
                table: "tracked_instruments",
                column: "active");

            // The runtime role (xd_app) reads and writes tracking rows with plain DML; it never changes the table.
            migrationBuilder.GrantTableToAppRole(Schemas.Market, "tracked_instruments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tracked_instruments",
                schema: "market");
        }
    }
}
