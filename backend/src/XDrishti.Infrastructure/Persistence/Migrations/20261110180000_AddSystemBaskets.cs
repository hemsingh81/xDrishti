using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// System baskets (BK-01, ADR 0254): <c>kind</c> and <c>system</c> on baskets, provenance and the source of a membership row, the catalogue <c>basket_sources</c> and the insert-only fetch log <c>basket_fetches</c>.
    /// The catalogue of the 26 NSE index lists is seeded here as data, every source DISABLED (no network is involved); file names are data, never derived. The migration holds its own copy of the list (a migration never changes).
    /// </summary>
    public partial class AddSystemBaskets : Migration
    {
        private const string BaseA = "https://nsearchives.nseindia.com/content/indices/";

        private const string BaseB = "https://niftyindices.com/IndexConstituent/";

        // name, file, host (A = nsearchives, B = niftyindices, AB = A with B as the alternate), members (the bound is exactly this; a trailing-line quirk is widened only when evidenced at ingest).
        private static readonly (string Name, string File, string Hosts, int Expected)[] Catalogue =
        [
            ("NSE Nifty 50", "ind_nifty50list.csv", "AB", 50),
            ("NSE Nifty Next 50", "ind_niftynext50list.csv", "A", 50),
            ("NSE Nifty 100", "ind_nifty100list.csv", "A", 100),
            ("NSE Nifty 200", "ind_nifty200list.csv", "A", 200),
            ("NSE Nifty 500", "ind_nifty500list.csv", "A", 500),
            ("NSE Nifty Midcap 150", "ind_niftymidcap150list.csv", "A", 150),
            ("NSE Nifty Smallcap 250", "ind_niftysmallcap250list.csv", "A", 250),
            ("NSE Nifty Microcap 250", "ind_niftymicrocap250_list.csv", "B", 250),
            ("NSE Nifty Bank", "ind_niftybanklist.csv", "A", 14),
            ("NSE Nifty Financial Services", "ind_niftyfinancelist.csv", "B", 20),
            ("NSE Nifty IT", "ind_niftyitlist.csv", "A", 10),
            ("NSE Nifty Pharma", "ind_niftypharmalist.csv", "A", 20),
            ("NSE Nifty Auto", "ind_niftyautolist.csv", "A", 15),
            ("NSE Nifty FMCG", "ind_niftyfmcglist.csv", "A", 15),
            ("NSE Nifty Metal", "ind_niftymetallist.csv", "A", 15),
            ("NSE Nifty Energy", "ind_niftyenergylist.csv", "A", 40),
            ("NSE Nifty Realty", "ind_niftyrealtylist.csv", "A", 10),
            ("NSE Nifty PSU Bank", "ind_niftypsubanklist.csv", "B", 12),
            ("NSE Nifty Oil & Gas", "ind_niftyoilgaslist.csv", "B", 15),
            ("NSE Nifty Healthcare", "ind_niftyhealthcarelist.csv", "A", 20),
            ("NSE Nifty Consumer Durables", "ind_niftyconsumerdurableslist.csv", "A", 14),
            ("NSE Nifty Media", "ind_niftymedialist.csv", "B", 10),
            ("NSE Nifty200 Momentum 30", "ind_nifty200momentum30_list.csv", "B", 30),
            ("NSE Nifty100 Quality 30", "ind_nifty100Quality30list.csv", "B", 30),
            ("NSE Nifty100 Low Volatility 30", "ind_nifty100LowVolatility30list.csv", "B", 30),
            ("NSE Nifty200 Alpha 30", "ind_nifty200alpha30_list.csv", "B", 30),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "kind",
                schema: "market",
                table: "baskets",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "manual");

            migrationBuilder.AddColumn<bool>(
                name: "system",
                schema: "market",
                table: "baskets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "detected_at",
                schema: "market",
                table: "basket_members",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "provenance",
                schema: "market",
                table: "basket_members",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "manual");

            migrationBuilder.AddColumn<string>(
                name: "source_kind",
                schema: "market",
                table: "basket_members",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "source_ref",
                schema: "market",
                table: "basket_members",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "basket_fetches",
                schema: "market",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    basket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    http_status = table.Column<int>(type: "integer", nullable: true),
                    etag = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    last_modified = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    rows = table.Column<int>(type: "integer", nullable: false),
                    outcome = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    added = table.Column<int>(type: "integer", nullable: false),
                    removed = table.Column<int>(type: "integer", nullable: false),
                    unmatched = table.Column<int>(type: "integer", nullable: false),
                    warnings = table.Column<string>(type: "jsonb", nullable: false),
                    error = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    forced = table.Column<bool>(type: "boolean", nullable: false),
                    actor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    settings_snapshot = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_basket_fetches", x => x.id);
                    table.CheckConstraint("ck_basket_fetches_outcome", "outcome IN ('unchanged', 'applied', 'rejected', 'failed', 'skipped')");
                    table.ForeignKey(
                        name: "fk_basket_fetches_baskets_basket_id",
                        column: x => x.basket_id,
                        principalSchema: "market",
                        principalTable: "baskets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "basket_sources",
                schema: "market",
                columns: table => new
                {
                    basket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_url = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    file_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    host_alt = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    format_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    cadence = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    expected_min = table.Column<int>(type: "integer", nullable: false),
                    expected_max = table.Column<int>(type: "integer", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    license_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    state = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "never"),
                    last_ok_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    etag = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    last_modified = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    fail_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_basket_sources", x => x.basket_id);
                    table.CheckConstraint("ck_basket_sources_bounds", "expected_min >= 0 AND expected_max >= expected_min");
                    table.CheckConstraint("ck_basket_sources_state", "state IN ('ok', 'stale', 'rejected', 'failed', 'never')");
                    table.ForeignKey(
                        name: "fk_basket_sources_baskets_basket_id",
                        column: x => x.basket_id,
                        principalSchema: "market",
                        principalTable: "baskets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_baskets_kind",
                schema: "market",
                table: "baskets",
                sql: "kind IN ('manual', 'index', 'list', 'derived')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_baskets_kind_system",
                schema: "market",
                table: "baskets",
                sql: "(kind = 'manual') = (NOT system)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_basket_members_provenance",
                schema: "market",
                table: "basket_members",
                sql: "provenance IN ('fetched', 'imported', 'recomputed', 'derived', 'manual')");

            migrationBuilder.CreateIndex(
                name: "ix_basket_fetches_basket_id_fetched_at",
                schema: "market",
                table: "basket_fetches",
                columns: new[] { "basket_id", "fetched_at" });

            // The fetch log is insert-only for the runtime role (the schema grant gave it UPDATE and DELETE).
            migrationBuilder.MakeAppendOnlyForAppRole("market", "basket_fetches");

            foreach (var (name, file, hosts, expected) in Catalogue)
            {
                var url = (hosts == "B" ? BaseB : BaseA) + file;
                var alt = hosts == "AB" ? "'" + BaseB + file + "'" : "NULL";
                var id = "md5('market.basket:" + file + "')::uuid";
                migrationBuilder.Sql(
                    "INSERT INTO market.baskets (id, name, colour, purpose, description, position, created_at, revision, kind, system) VALUES ("
                    + id + ", '" + name + "', '#64748b', 'watch', 'Official NSE index list; read-only, membership from the fetched file.', 0, now(), 0, 'index', true)");
                migrationBuilder.Sql(
                    "INSERT INTO market.basket_sources (basket_id, source_url, file_name, host_alt, format_version, cadence, expected_min, expected_max, enabled, state, fail_count) VALUES ("
                    + id + ", '" + url + "', '" + file + "', " + alt + ", 'nse-index-csv-1', 'daily', " + expected + ", " + expected + ", false, 'never', 0)");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "basket_fetches",
                schema: "market");

            migrationBuilder.DropTable(
                name: "basket_sources",
                schema: "market");

            migrationBuilder.Sql("DELETE FROM market.basket_members WHERE basket_id IN (SELECT id FROM market.baskets WHERE system)");
            migrationBuilder.Sql("DELETE FROM market.baskets WHERE system");

            migrationBuilder.DropCheckConstraint(
                name: "ck_baskets_kind",
                schema: "market",
                table: "baskets");

            migrationBuilder.DropCheckConstraint(
                name: "ck_baskets_kind_system",
                schema: "market",
                table: "baskets");

            migrationBuilder.DropCheckConstraint(
                name: "ck_basket_members_provenance",
                schema: "market",
                table: "basket_members");

            migrationBuilder.DropColumn(
                name: "kind",
                schema: "market",
                table: "baskets");

            migrationBuilder.DropColumn(
                name: "system",
                schema: "market",
                table: "baskets");

            migrationBuilder.DropColumn(
                name: "detected_at",
                schema: "market",
                table: "basket_members");

            migrationBuilder.DropColumn(
                name: "provenance",
                schema: "market",
                table: "basket_members");

            migrationBuilder.DropColumn(
                name: "source_kind",
                schema: "market",
                table: "basket_members");

            migrationBuilder.DropColumn(
                name: "source_ref",
                schema: "market",
                table: "basket_members");
        }
    }
}
