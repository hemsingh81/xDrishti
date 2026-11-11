using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Relative strength (BK-09, ADR 0263): the momentum rank and index-relative rows per day, share and basket (<c>relative_strength_daily</c>; the index-relative row has the empty basket id) and the equal-weight sector return and rank (<c>sector_strength_daily</c>).
    /// Both are insert-only for the runtime role.
    /// </summary>
    public partial class AddRelativeStrengthDaily : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "relative_strength_daily",
                schema: "market",
                columns: table => new
                {
                    as_of = table.Column<DateOnly>(type: "date", nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    basket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lookback = table.Column<int>(type: "integer", nullable: false),
                    skip = table.Column<int>(type: "integer", nullable: false),
                    score = table.Column<double>(type: "double precision", nullable: true),
                    pct_rank = table.Column<double>(type: "double precision", nullable: true),
                    n_ranked = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    membership_basis = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_relative_strength_daily", x => new { x.as_of, x.instrument_id, x.basket_id, x.lookback, x.skip });
                    table.CheckConstraint("ck_relative_strength_daily_basis", "membership_basis IN ('dated', 'single_snapshot', 'none')");
                    table.CheckConstraint("ck_relative_strength_daily_rank", "pct_rank IS NULL OR (pct_rank >= 0 AND pct_rank <= 1)");
                    table.CheckConstraint("ck_relative_strength_daily_state", "state IN ('value', 'insufficient', 'not_evaluable', 'unavailable', 'short_history', 'corporate_action_unresolved')");
                    table.ForeignKey(
                        name: "fk_relative_strength_daily_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalSchema: "market",
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sector_strength_daily",
                schema: "market",
                columns: table => new
                {
                    as_of = table.Column<DateOnly>(type: "date", nullable: false),
                    sector = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    window_sessions = table.Column<int>(type: "integer", nullable: false),
                    ret = table.Column<double>(type: "double precision", nullable: true),
                    rank = table.Column<int>(type: "integer", nullable: true),
                    n_members = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sector_strength_daily", x => new { x.as_of, x.sector, x.window_sessions });
                    table.CheckConstraint("ck_sector_strength_daily_rank", "rank IS NULL OR rank >= 1");
                    table.CheckConstraint("ck_sector_strength_daily_state", "state IN ('value', 'insufficient', 'not_evaluable', 'unavailable', 'short_history', 'corporate_action_unresolved')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_relative_strength_daily_basket_id_as_of",
                schema: "market",
                table: "relative_strength_daily",
                columns: new[] { "basket_id", "as_of" });

            migrationBuilder.CreateIndex(
                name: "ix_relative_strength_daily_instrument_id_as_of",
                schema: "market",
                table: "relative_strength_daily",
                columns: new[] { "instrument_id", "as_of" });

            // Insert-once (BK-09): the runtime role reads and inserts, nothing else (default privileges gave it UPDATE and DELETE).
            migrationBuilder.MakeAppendOnlyForAppRole("market", "relative_strength_daily");
            migrationBuilder.MakeAppendOnlyForAppRole("market", "sector_strength_daily");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "relative_strength_daily",
                schema: "market");

            migrationBuilder.DropTable(
                name: "sector_strength_daily",
                schema: "market");
        }
    }
}
