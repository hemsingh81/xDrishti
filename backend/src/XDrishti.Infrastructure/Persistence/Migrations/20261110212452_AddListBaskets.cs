using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// List baskets (BK-05, ADR 0256): the dated F&amp;O lot sizes <c>market.fo_lot_sizes</c> (insert-only for the runtime role) and the seeded list baskets (kind list, system) with their catalogue rows. The four lists read from the Dhan master are enabled (no network); the two NSE F&amp;O
    /// files are seeded DISABLED. The lot-size file has a catalogue row and a fetch log, so it has a basket row too: archived, no members, shown nowhere. The migration holds its own copy of the seed (a migration never changes); the ids are <c>md5('market.basket:list:' || key)::uuid</c>.
    /// </summary>
    public partial class AddListBaskets : Migration
    {
        private const string BaseFo = "https://nsearchives.nseindia.com/content/fo/";

        // key, name, description, format, url (empty = read from the master), file, cadence, expected min, expected max, enabled, archived.
        private static readonly (string Key, string Name, string Description, string Format, string Url, string File, string Cadence, int Min, int Max, bool Enabled, bool Archived)[] Lists =
        [
            ("fno-stocks", "List: F&O stocks", "Shares with stock futures in the Dhan instrument master; membership dated from the first recorded master refresh.", "dhan-master-flags-1", "", "instrument-master", "master", 0, 100000, true, false),
            ("asm-gsm", "List: ASM/GSM surveillance", "Shares the Dhan master flags for ASM or GSM surveillance (the category is the member note); dated from the first recorded master refresh.", "dhan-master-flags-1", "", "instrument-master", "master", 0, 100000, true, false),
            ("mtf-eligible", "List: MTF-eligible", "Shares the broker reports as MTF-eligible (broker-reported, may change); no leverage figure is shown or assumed.", "dhan-master-flags-1", "", "instrument-master", "master", 0, 100000, true, false),
            ("series-t2t", "List: SME and trade-for-trade series", "Shares in the series BE, BZ, SM or ST in the Dhan instrument master; dated from the first recorded master refresh.", "dhan-master-flags-1", "", "instrument-master", "master", 0, 100000, true, false),
            ("fo-ban", "List: In F&O ban", "Excluded from new positions: ban period. Securities in the F&O ban for the trade date named in NSE's file; an exclusion filter, not a tradable universe.", "nse-fo-secban-1", BaseFo + "fo_secban.csv", "fo_secban.csv", "ban", 0, 200, false, false),
            ("fo-lot-sizes", "List: F&O lot sizes (source)", "The source row of NSE's F&O lot-size file; holds no members.", "nse-fo-mktlots-1", BaseFo + "fo_mktlots.csv", "fo_mktlots.csv", "lots", 100, 400, false, true),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fo_lot_sizes",
                schema: "market",
                columns: table => new
                {
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    lot_size = table.Column<int>(type: "integer", nullable: false),
                    expiry_label = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    fetch_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fo_lot_sizes", x => new { x.instrument_id, x.effective_from });
                    table.CheckConstraint("ck_fo_lot_sizes_lot_size", "lot_size > 0");
                    table.ForeignKey(
                        name: "fk_fo_lot_sizes_basket_fetches_fetch_id",
                        column: x => x.fetch_id,
                        principalSchema: "market",
                        principalTable: "basket_fetches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fo_lot_sizes_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalSchema: "market",
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fo_lot_sizes_fetch_id",
                schema: "market",
                table: "fo_lot_sizes",
                column: "fetch_id");

            // The dated lot sizes are insert-only for the runtime role (the schema grant gave it UPDATE and DELETE).
            migrationBuilder.MakeAppendOnlyForAppRole("market", "fo_lot_sizes");

            foreach (var (key, name, description, format, url, file, cadence, min, max, enabled, archived) in Lists)
            {
                var id = "md5('market.basket:list:" + key + "')::uuid";
                migrationBuilder.Sql(
                    "INSERT INTO market.baskets (id, name, colour, purpose, description, position, created_at, archived_at, revision, kind, system) VALUES ("
                    + id + ", " + Quote(name) + ", '#64748b', 'watch', " + Quote(description) + ", 0, now(), " + (archived ? "now()" : "NULL") + ", 0, 'list', true)");
                migrationBuilder.Sql(
                    "INSERT INTO market.basket_sources (basket_id, source_url, file_name, host_alt, format_version, cadence, expected_min, expected_max, enabled, state, fail_count) VALUES ("
                    + id + ", " + Quote(url) + ", " + Quote(file) + ", NULL, " + Quote(format) + ", " + Quote(cadence) + ", " + min + ", " + max + ", " + (enabled ? "true" : "false") + ", 'never', 0)");
            }
        }

        private static string Quote(string text) => "'" + text.Replace("'", "''", StringComparison.Ordinal) + "'";

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM market.basket_members WHERE basket_id IN (SELECT id FROM market.baskets WHERE kind = 'list')");
            migrationBuilder.DropTable(
                name: "fo_lot_sizes",
                schema: "market");

            migrationBuilder.Sql("DELETE FROM market.basket_fetches WHERE basket_id IN (SELECT id FROM market.baskets WHERE kind = 'list')");
            migrationBuilder.Sql("DELETE FROM market.basket_sources WHERE basket_id IN (SELECT id FROM market.baskets WHERE kind = 'list')");
            migrationBuilder.Sql("DELETE FROM market.baskets WHERE kind = 'list'");
        }
    }
}
