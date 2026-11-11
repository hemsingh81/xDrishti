using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHistoryBackfill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // CF-04 Part B, ADR 0270: the resumable chunk ledger of the 1-minute backfill and the per-instrument seam record. Raw SQL on purpose: nothing maps these tables in the EF model; the backfill writes with SQL.
            migrationBuilder.Sql("""
                CREATE TABLE market.backfill_chunks (
                    instrument_id uuid NOT NULL REFERENCES market.instruments (id) ON DELETE CASCADE,
                    from_date date NOT NULL,
                    to_date date NOT NULL,
                    status varchar(8) NOT NULL,
                    row_count integer NOT NULL DEFAULT 0,
                    attempts integer NOT NULL DEFAULT 0,
                    error varchar(200) NULL,
                    updated_at timestamptz NOT NULL,
                    CONSTRAINT pk_backfill_chunks PRIMARY KEY (instrument_id, from_date),
                    CONSTRAINT ck_backfill_chunks_window CHECK (from_date < to_date),
                    CONSTRAINT ck_backfill_chunks_status CHECK (status IN ('planned', 'done', 'failed', 'empty')),
                    CONSTRAINT ck_backfill_chunks_counts CHECK (row_count >= 0 AND attempts >= 0)
                )
                """);
            migrationBuilder.Sql("CREATE INDEX ix_backfill_chunks_unfinished ON market.backfill_chunks (instrument_id) WHERE status IN ('planned', 'failed')");

            // The seam is the start of the oldest stored bar with source csv, re-read on every run; the row says what the planner and the writer last saw, the overlap probe's outcome and whether the continuity check found a jump.
            migrationBuilder.Sql("""
                CREATE TABLE market.backfill_seams (
                    instrument_id uuid NOT NULL REFERENCES market.instruments (id) ON DELETE CASCADE,
                    seam_at timestamptz NOT NULL,
                    high_water_at timestamptz NOT NULL,
                    probe_status varchar(10) NOT NULL DEFAULT 'none',
                    probed_at timestamptz NULL,
                    median_bps double precision NULL,
                    within_max_pct double precision NULL,
                    detail varchar(200) NULL,
                    seam_jump boolean NOT NULL DEFAULT false,
                    jump_pct double precision NULL,
                    updated_at timestamptz NOT NULL,
                    CONSTRAINT pk_backfill_seams PRIMARY KEY (instrument_id),
                    CONSTRAINT ck_backfill_seams_probe CHECK (probe_status IN ('none', 'passed', 'unresolved'))
                )
                """);

            // The cut every research, learning and report read applies: bars before the HIGH-WATER seam (the latest seam ever recorded, never lowered) are Dhan's raw backfill. A seam that moves earlier cannot admit them again. -infinity when
            // the instrument has no row. The daily readers filter with it in SQL; the 1-minute readers use SeamAwareBars.
            migrationBuilder.Sql("""
                CREATE FUNCTION market.backfill_cut(p_instrument uuid)
                RETURNS timestamptz
                LANGUAGE sql STABLE
                SET search_path = pg_catalog, market, pg_temp
                AS $$ SELECT coalesce((SELECT s.high_water_at FROM market.backfill_seams s WHERE s.instrument_id = p_instrument), '-infinity'::timestamptz) $$
                """);

            // As ADR 0267 / AddBasketToolAccess: no function of schema market is executable by PUBLIC or the read roles; the runtime role is the only caller.
            migrationBuilder.Sql("REVOKE EXECUTE ON FUNCTION market.backfill_cut(uuid) FROM PUBLIC");
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_app') THEN
                        GRANT EXECUTE ON FUNCTION market.backfill_cut(uuid) TO xd_app;
                    END IF;
                END $$
                """);

            // The runtime role reads, inserts, updates and may delete: both tables are a work ledger, not data (a planned chunk that the plan no longer has is removed). The bars themselves are never deleted (bars_1m keeps its own grant).
            foreach (var table in new[] { "backfill_chunks", "backfill_seams" })
            {
                migrationBuilder.GrantTableToAppRole(Schemas.Market, table);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS market.backfill_cut(uuid)");
            migrationBuilder.Sql("DROP TABLE IF EXISTS market.backfill_seams");
            migrationBuilder.Sql("DROP TABLE IF EXISTS market.backfill_chunks");
        }
    }
}
