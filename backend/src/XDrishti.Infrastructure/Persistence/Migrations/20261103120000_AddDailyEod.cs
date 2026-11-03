using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDailyEod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // CF-04 Part A, ADR 0195: NSE's official end-of-day rows (a different thing from the minute aggregate) and the ledger of the files behind them. Raw SQL on purpose: nothing maps these tables in the EF model; the loader writes with SQL.
            migrationBuilder.Sql("""
                CREATE TABLE market.daily_eod (
                    trade_date date NOT NULL,
                    series varchar(8) NOT NULL,
                    symbol varchar(40) NOT NULL,
                    isin varchar(16) NULL,
                    open numeric(18,4) NOT NULL,
                    high numeric(18,4) NOT NULL,
                    low numeric(18,4) NOT NULL,
                    close numeric(18,4) NOT NULL,
                    prev_close numeric(18,4) NULL,
                    traded_qty bigint NOT NULL,
                    turnover numeric(22,2) NOT NULL,
                    trades bigint NULL,
                    delivery_qty bigint NULL,
                    delivery_pct numeric(7,2) NULL,
                    source varchar(10) NOT NULL,
                    file_sha256 char(64) NOT NULL,
                    loaded_at timestamptz NOT NULL,
                    CONSTRAINT pk_daily_eod PRIMARY KEY (trade_date, series, symbol),
                    CONSTRAINT ck_daily_eod_prices CHECK (open >= 0 AND high >= 0 AND low >= 0 AND close >= 0),
                    CONSTRAINT ck_daily_eod_qty CHECK (traded_qty >= 0 AND turnover >= 0 AND (trades IS NULL OR trades >= 0)),
                    CONSTRAINT ck_daily_eod_delivery CHECK ((delivery_qty IS NULL) = (delivery_pct IS NULL) AND (delivery_qty IS NULL OR (delivery_qty >= 0 AND delivery_qty <= traded_qty AND delivery_pct BETWEEN 0 AND 100))),
                    CONSTRAINT ck_daily_eod_source CHECK (source IN ('nse_cm')),
                    CONSTRAINT ck_daily_eod_sha CHECK (file_sha256 ~ '^[0-9a-f]{64}$')
                )
                """);
            migrationBuilder.Sql("CREATE INDEX ix_daily_eod_symbol ON market.daily_eod (symbol, series, trade_date)");
            migrationBuilder.Sql("CREATE INDEX ix_daily_eod_isin ON market.daily_eod (isin, trade_date) WHERE isin IS NOT NULL");

            migrationBuilder.Sql("""
                CREATE TABLE market.eod_files (
                    trade_date date NOT NULL,
                    kind varchar(8) NOT NULL,
                    status varchar(8) NOT NULL,
                    sha256 char(64) NULL,
                    row_count integer NOT NULL,
                    fetched_at timestamptz NOT NULL,
                    CONSTRAINT pk_eod_files PRIMARY KEY (trade_date, kind),
                    CONSTRAINT ck_eod_files_kind CHECK (kind IN ('bhav', 'delivery')),
                    CONSTRAINT ck_eod_files_status CHECK (status IN ('loaded', 'absent')),
                    CONSTRAINT ck_eod_files_sha CHECK ((status = 'loaded') = (sha256 IS NOT NULL))
                )
                """);

            // The securities whose span of rows [first date, last date] holds the date (a halted day inside the span does not remove a name). A date after the last loaded day is read as that day, and one more than a week after it (or before the
            // first) answers empty: an unloaded range is not answered from stale data. Dead names are listed; in_catalog says whether the ISIN is in the instrument master. Nothing here adds anything to the tracked instruments.
            migrationBuilder.Sql("""
                CREATE FUNCTION market.eod_universe(as_of date)
                RETURNS TABLE (isin varchar, symbol varchar, series varchar, known_since date, last_seen date, in_catalog boolean)
                LANGUAGE sql STABLE
                SET search_path = pg_catalog, pg_temp
                AS $$
                    WITH bounds AS (SELECT min(b.trade_date) AS lo, max(b.trade_date) AS hi FROM market.daily_eod b),
                    eff AS (SELECT CASE WHEN as_of < lo OR as_of > hi + 7 THEN NULL ELSE least(as_of, hi) END AS d FROM bounds),
                    spans AS (
                        SELECT e.symbol, e.series, max(e.isin) AS isin, min(e.trade_date) AS known_since, max(e.trade_date) AS last_seen
                        FROM market.daily_eod e
                        GROUP BY e.symbol, e.series)
                    SELECT s.isin::varchar, s.symbol::varchar, s.series::varchar, s.known_since, s.last_seen,
                           (s.isin IS NOT NULL AND EXISTS (SELECT 1 FROM market.instruments i WHERE i.isin = s.isin))
                    FROM spans s CROSS JOIN eff
                    WHERE eff.d BETWEEN s.known_since AND s.last_seen
                $$
                """);

            // The runtime role reads, inserts and updates; it never deletes a row of either table (the grant is per table, DELETE taken away again).
            foreach (var table in new[] { "daily_eod", "eod_files" })
            {
                migrationBuilder.GrantTableToAppRole(Schemas.Market, table);
                migrationBuilder.NeverDeleteForAppRole(Schemas.Market, table);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS market.eod_universe(date)");
            migrationBuilder.Sql("DROP TABLE IF EXISTS market.eod_files");
            migrationBuilder.Sql("DROP TABLE IF EXISTS market.daily_eod");
        }
    }
}
