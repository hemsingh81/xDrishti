using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLiveFeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_holdings_last_price_source",
                schema: "portfolio",
                table: "holdings");

            migrationBuilder.AddColumn<string>(
                name: "details",
                schema: "platform",
                table: "service_heartbeats",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "state",
                schema: "platform",
                table: "service_heartbeats",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "live_quotes",
                schema: "market",
                columns: table => new
                {
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    tick_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_quotes", x => x.instrument_id);
                    table.CheckConstraint("ck_live_quotes_last_price", "last_price > 0");
                    table.ForeignKey(
                        name: "fk_live_quotes_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalSchema: "market",
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("Npgsql:UnloggedTable", true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_holdings_last_price_source",
                schema: "portfolio",
                table: "holdings",
                sql: "last_price_source IN ('sync', 'quote', 'live')");

            // The live prices of instruments that are not holdings (ADR 0024 §3): the runtime role reads, inserts and updates the rows (an upsert every few seconds) and has no DELETE - and no TRUNCATE, which PostgreSQL
            // grants separately. The table is emptied at the start of each feed window by this one function, with its owner's rights (SECURITY DEFINER; the migration runs as xd_owner, which owns the table, the same pattern as
            // market.undo_csv_bars). It takes no argument and does nothing but empty this table; the search path is pinned (system schema first, temporary objects last) and the table is schema-qualified. Only xd_app may execute it.
            migrationBuilder.GrantTableToAppRole(Schemas.Market, "live_quotes");
            migrationBuilder.NeverDeleteForAppRole(Schemas.Market, "live_quotes");
            migrationBuilder.Sql("""
                CREATE FUNCTION market.clear_live_quotes()
                RETURNS void
                LANGUAGE plpgsql
                SECURITY DEFINER
                SET search_path = pg_catalog, market, pg_temp
                AS $$
                BEGIN
                    TRUNCATE TABLE market.live_quotes;
                END;
                $$;
                """);
            migrationBuilder.RestrictFunctionToAppRole("market.clear_live_quotes()");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION market.clear_live_quotes()");

            migrationBuilder.DropTable(
                name: "live_quotes",
                schema: "market");

            migrationBuilder.DropCheckConstraint(
                name: "ck_holdings_last_price_source",
                schema: "portfolio",
                table: "holdings");

            migrationBuilder.DropColumn(
                name: "details",
                schema: "platform",
                table: "service_heartbeats");

            migrationBuilder.DropColumn(
                name: "state",
                schema: "platform",
                table: "service_heartbeats");

            // a live price becomes a quote again: the nearest older source (the price time stays)
            migrationBuilder.Sql("UPDATE portfolio.holdings SET last_price_source = 'quote' WHERE last_price_source = 'live'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_holdings_last_price_source",
                schema: "portfolio",
                table: "holdings",
                sql: "last_price_source IN ('sync', 'quote')");
        }
    }
}
