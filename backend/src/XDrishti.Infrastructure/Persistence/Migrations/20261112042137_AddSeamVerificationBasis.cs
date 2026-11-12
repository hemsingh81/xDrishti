using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSeamVerificationBasis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // CF-04d, ADR 0273: the basis (SHA-256 hex of the seam instant and the applied actions) the overlap probe passed on and the one the continuity check last ran on. Null: the probe has not passed, or the check never ran. Raw SQL on purpose: nothing maps these tables in the EF model.
            // Grants are unchanged: the runtime role already holds table-level SELECT, INSERT, UPDATE and DELETE on backfill_seams (AddHistoryBackfill) and a new column falls under them.
            migrationBuilder.Sql("ALTER TABLE market.backfill_seams ADD COLUMN probe_basis char(64) NULL, ADD COLUMN continuity_basis char(64) NULL");

            // The cut of the daily readers, and of SeamAwareBars, is now by date: India midnight of the high-water seam date, so the seam day is a pure CSV day and its day bucket (which starts at India midnight) is no longer dropped. CREATE OR REPLACE keeps the grants.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION market.backfill_cut(p_instrument uuid)
                RETURNS timestamptz
                LANGUAGE sql STABLE
                SET search_path = pg_catalog, market, pg_temp
                AS $$ SELECT coalesce((SELECT date_trunc('day', s.high_water_at AT TIME ZONE 'Asia/Kolkata') AT TIME ZONE 'Asia/Kolkata' FROM market.backfill_seams s WHERE s.instrument_id = p_instrument), '-infinity'::timestamptz) $$
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION market.backfill_cut(p_instrument uuid)
                RETURNS timestamptz
                LANGUAGE sql STABLE
                SET search_path = pg_catalog, market, pg_temp
                AS $$ SELECT coalesce((SELECT s.high_water_at FROM market.backfill_seams s WHERE s.instrument_id = p_instrument), '-infinity'::timestamptz) $$
                """);
            migrationBuilder.Sql("ALTER TABLE market.backfill_seams DROP COLUMN IF EXISTS continuity_basis, DROP COLUMN IF EXISTS probe_basis");
        }
    }
}
