using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCsvSeamIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // CF-04 Part B, ADR 0270: the backfill reads each instrument's seam, the oldest bar of source csv, on every run. Once Dhan bars sit before the CSV bars, a scan in time order would walk past them; this partial index
            // (CSV rows only) answers "oldest csv bar of the instrument" from its first entry. Raw SQL on purpose: bars_1m is a hypertable that nothing maps for indexes.
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS ix_bars_1m_csv ON market.bars_1m (instrument_id, ts) WHERE source = 'csv'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS market.ix_bars_1m_csv");
        }
    }
}
