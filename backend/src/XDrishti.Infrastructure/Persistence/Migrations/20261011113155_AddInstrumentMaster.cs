using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInstrumentMaster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // P1-03 / ADR 0010: the instrument master (schema market). Needs the pg_trgm extension (trigram indexes for search).
            migrationBuilder.EnsureSchema(
                name: "market");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .Annotation("Npgsql:PostgresExtension:timescaledb", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:timescaledb", ",,");

            migrationBuilder.CreateTable(
                name: "instruments",
                schema: "market",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    canonical_symbol = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    exchange = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    exchange_segment = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    type = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    isin = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    series = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    lot_size = table.Column<int>(type: "integer", nullable: false),
                    tick_size = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: true),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    underlying_symbol = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    sector = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    sector_source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    fno_eligible = table.Column<bool>(type: "boolean", nullable: false),
                    asm_gsm_flag = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    asm_gsm_category = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    mtf_leverage = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: true),
                    freeze_qty = table.Column<long>(type: "bigint", nullable: true),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    retired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_instruments", x => x.id);
                    table.CheckConstraint("ck_instruments_type", "type IN ('share', 'etf', 'index', 'future')");
                });

            migrationBuilder.CreateTable(
                name: "master_refreshes",
                schema: "market",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trigger = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    force = table.Column<bool>(type: "boolean", nullable: false),
                    requested_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    phase = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    rows_read = table.Column<long>(type: "bigint", nullable: false),
                    dhan_etag = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    dhan_last_modified = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    sector_etag = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    sector_last_modified = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    added = table.Column<int>(type: "integer", nullable: false),
                    updated = table.Column<int>(type: "integer", nullable: false),
                    retired = table.Column<int>(type: "integer", nullable: false),
                    revived = table.Column<int>(type: "integer", nullable: false),
                    unchanged = table.Column<int>(type: "integer", nullable: false),
                    skipped_by_scope = table.Column<int>(type: "integer", nullable: false),
                    collisions = table.Column<int>(type: "integer", nullable: false),
                    sector_matched = table.Column<int>(type: "integer", nullable: false),
                    sector_unmatched = table.Column<int>(type: "integer", nullable: false),
                    fno_underlyings = table.Column<int>(type: "integer", nullable: false),
                    total_active = table.Column<int>(type: "integer", nullable: false),
                    kept_by_type = table.Column<string>(type: "jsonb", nullable: false),
                    warnings = table.Column<string>(type: "jsonb", nullable: false),
                    error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    rules_version = table.Column<int>(type: "integer", nullable: false),
                    sectors_applied = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_master_refreshes", x => x.id);
                    table.CheckConstraint("ck_master_refreshes_status", "status IN ('running', 'succeeded', 'unchanged', 'failed')");
                    table.CheckConstraint("ck_master_refreshes_trigger", "trigger IN ('manual', 'daily', 'forced')");
                });

            migrationBuilder.CreateTable(
                name: "instrument_broker_ids",
                schema: "market",
                columns: table => new
                {
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    broker = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    broker_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    segment = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_instrument_broker_ids", x => new { x.instrument_id, x.broker });
                    table.ForeignKey(
                        name: "fk_instrument_broker_ids_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalSchema: "market",
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_instrument_broker_ids_broker_broker_id_segment",
                schema: "market",
                table: "instrument_broker_ids",
                columns: new[] { "broker", "broker_id", "segment" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_instruments_canonical_symbol",
                schema: "market",
                table: "instruments",
                column: "canonical_symbol",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_instruments_canonical_symbol_trgm",
                schema: "market",
                table: "instruments",
                column: "canonical_symbol")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_instruments_display_name_trgm",
                schema: "market",
                table: "instruments",
                column: "display_name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_instruments_fno_eligible",
                schema: "market",
                table: "instruments",
                column: "fno_eligible");

            migrationBuilder.CreateIndex(
                name: "ix_instruments_isin_trgm",
                schema: "market",
                table: "instruments",
                column: "isin")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_instruments_name_trgm",
                schema: "market",
                table: "instruments",
                column: "name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_instruments_retired_at",
                schema: "market",
                table: "instruments",
                column: "retired_at");

            migrationBuilder.CreateIndex(
                name: "ix_instruments_sector",
                schema: "market",
                table: "instruments",
                column: "sector");

            migrationBuilder.CreateIndex(
                name: "ix_instruments_type",
                schema: "market",
                table: "instruments",
                column: "type");

            migrationBuilder.CreateIndex(
                name: "ix_master_refreshes_started_at",
                schema: "market",
                table: "master_refreshes",
                column: "started_at");

            migrationBuilder.CreateIndex(
                name: "ux_master_refreshes_one_running",
                schema: "market",
                table: "master_refreshes",
                column: "status",
                unique: true,
                filter: "status = 'running'");

            migrationBuilder.GrantSchemaUsageToAppRole(Schemas.Market);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "instrument_broker_ids",
                schema: "market");

            migrationBuilder.DropTable(
                name: "master_refreshes",
                schema: "market");

            migrationBuilder.DropTable(
                name: "instruments",
                schema: "market");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:timescaledb", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:timescaledb", ",,");
        }
    }
}
