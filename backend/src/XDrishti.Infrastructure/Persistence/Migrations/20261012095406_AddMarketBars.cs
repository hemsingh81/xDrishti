using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketBars : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bar_coverage",
                schema: "market",
                columns: table => new
                {
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    first_bar = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_bar = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    bars = table.Column<long>(type: "bigint", nullable: false),
                    csv_bars = table.Column<long>(type: "bigint", nullable: false),
                    dhan_bars = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    dirty_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    dirty_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    aggregates_built_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    aggregates = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bar_coverage", x => x.instrument_id);
                    table.CheckConstraint("ck_bar_coverage_dirty", "(dirty_from IS NULL) = (dirty_to IS NULL) AND (dirty_from IS NULL OR dirty_from <= dirty_to)");
                    table.CheckConstraint("ck_bar_coverage_range", "first_bar <= last_bar AND bars > 0");
                    table.ForeignKey(
                        name: "fk_bar_coverage_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalSchema: "market",
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bars_1m",
                schema: "market",
                columns: table => new
                {
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ts = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    open = table.Column<double>(type: "double precision", nullable: false),
                    high = table.Column<double>(type: "double precision", nullable: false),
                    low = table.Column<double>(type: "double precision", nullable: false),
                    close = table.Column<double>(type: "double precision", nullable: false),
                    volume = table.Column<long>(type: "bigint", nullable: false),
                    source = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bars_1m", x => new { x.instrument_id, x.ts });
                    table.CheckConstraint("ck_bars_1m_ohlc", "low > 0 AND low <= open AND low <= close AND high >= open AND high >= close AND high >= low");
                    table.CheckConstraint("ck_bars_1m_source", "source IN ('csv', 'dhan')");
                    table.CheckConstraint("ck_bars_1m_volume", "volume >= 0");
                });

            migrationBuilder.CreateTable(
                name: "bars_agg",
                schema: "market",
                columns: table => new
                {
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    timeframe = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ts = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    open = table.Column<double>(type: "double precision", nullable: false),
                    high = table.Column<double>(type: "double precision", nullable: false),
                    low = table.Column<double>(type: "double precision", nullable: false),
                    close = table.Column<double>(type: "double precision", nullable: false),
                    volume = table.Column<long>(type: "bigint", nullable: false),
                    bars = table.Column<int>(type: "integer", nullable: false),
                    complete = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bars_agg", x => new { x.instrument_id, x.timeframe, x.ts });
                    table.CheckConstraint("ck_bars_agg_bars", "bars > 0");
                    table.CheckConstraint("ck_bars_agg_ohlc", "low > 0 AND low <= open AND low <= close AND high >= open AND high >= close AND high >= low");
                    table.CheckConstraint("ck_bars_agg_timeframe", "timeframe IN ('5m', '10m', '15m', '30m', '1h', '1D', '1W', '1M')");
                    table.CheckConstraint("ck_bars_agg_volume", "volume >= 0");
                });

            migrationBuilder.CreateTable(
                name: "csv_files",
                schema: "market",
                columns: table => new
                {
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rows = table.Column<long>(type: "bigint", nullable: false),
                    inserted = table.Column<long>(type: "bigint", nullable: false),
                    updated = table.Column<long>(type: "bigint", nullable: false),
                    repaired = table.Column<long>(type: "bigint", nullable: false),
                    rejected = table.Column<long>(type: "bigint", nullable: false),
                    loaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_csv_files", x => x.file_name);
                    table.CheckConstraint("ck_csv_files_counts", "rows >= 0 AND inserted >= 0 AND updated >= 0 AND repaired >= 0 AND rejected >= 0 AND size_bytes >= 0");
                    table.ForeignKey(
                        name: "fk_csv_files_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalSchema: "market",
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "data_jobs",
                schema: "market",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    steps = table.Column<string>(type: "jsonb", nullable: false),
                    force_csv = table.Column<bool>(type: "boolean", nullable: false),
                    rebuild_all = table.Column<bool>(type: "boolean", nullable: false),
                    current_phase = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: true),
                    csv = table.Column<string>(type: "jsonb", nullable: false),
                    dhan = table.Column<string>(type: "jsonb", nullable: false),
                    aggregate = table.Column<string>(type: "jsonb", nullable: false),
                    warnings = table.Column<string>(type: "jsonb", nullable: false),
                    error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_jobs", x => x.id);
                    table.CheckConstraint("ck_data_jobs_status", "status IN ('running', 'succeeded', 'failed')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_bar_coverage_dirty",
                schema: "market",
                table: "bar_coverage",
                column: "dirty_from",
                filter: "dirty_from IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_bar_coverage_last_bar",
                schema: "market",
                table: "bar_coverage",
                column: "last_bar");

            migrationBuilder.CreateIndex(
                name: "ix_csv_files_instrument_id",
                schema: "market",
                table: "csv_files",
                column: "instrument_id");

            migrationBuilder.CreateIndex(
                name: "ix_data_jobs_started_at",
                schema: "market",
                table: "data_jobs",
                column: "started_at");

            migrationBuilder.CreateIndex(
                name: "ux_data_jobs_one_running",
                schema: "market",
                table: "data_jobs",
                column: "status",
                unique: true,
                filter: "status = 'running'");

            // bars_1m is a TimescaleDB hypertable on the bar start: 1-month chunks, compressed after 7 days, one segment per instrument ordered by time (about 40 million
            // rows for 214 instruments over two years). The primary key (instrument, bar start) is the only index: the default index on the time column alone would only
            // slow the loads. Compressed chunks still accept the upserts of later loads and fetches (checked by the integration tests, also as the runtime role).
            migrationBuilder.Sql("SELECT create_hypertable('market.bars_1m', by_range('ts', INTERVAL '1 month'), create_default_indexes => false)");
            migrationBuilder.Sql("""
                ALTER TABLE market.bars_1m SET (
                    timescaledb.compress,
                    timescaledb.compress_segmentby = 'instrument_id',
                    timescaledb.compress_orderby = 'ts')
                """);
            migrationBuilder.Sql("SELECT add_compression_policy('market.bars_1m', INTERVAL '7 days')");

            // The runtime role (xd_app) reads and writes the data with plain DML but never deletes it (docs/design/06 §3.4 spirit: bars, aggregates and their records are kept
            // for good); it never changes the tables. Chunks are created by TimescaleDB with the owner's rights and the hypertable's privileges.
            foreach (var table in new[] { "bars_1m", "bars_agg", "bar_coverage", "csv_files", "data_jobs" })
            {
                migrationBuilder.GrantTableToAppRole(Schemas.Market, table);
                migrationBuilder.NeverDeleteForAppRole(Schemas.Market, table);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bar_coverage",
                schema: "market");

            migrationBuilder.DropTable(
                name: "bars_1m",
                schema: "market");

            migrationBuilder.DropTable(
                name: "bars_agg",
                schema: "market");

            migrationBuilder.DropTable(
                name: "csv_files",
                schema: "market");

            migrationBuilder.DropTable(
                name: "data_jobs",
                schema: "market");
        }
    }
}
