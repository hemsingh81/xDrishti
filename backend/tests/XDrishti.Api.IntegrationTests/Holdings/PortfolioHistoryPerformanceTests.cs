using System.Diagnostics;
using System.Globalization;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Holdings;

/// <summary>
/// The history of 300 holdings over two years of daily bars (P2-07: under 300 ms in the spec; this guard only fails on something grossly wrong, with a bound far above that, because the machine and the container vary).
/// Clock: Monday 2026-10-12 10:00 India time.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class PortfolioHistoryPerformanceTests(ApiFactory factory) : PortfolioHistoryTestBase(factory)
{
    private const int Holdings = 300;

    private static readonly TimeSpan GenerousBound = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task The_history_of_300_holdings_over_two_years_is_one_bars_statement_and_fast_enough()
    {
        // 300 shares in the master with Dhan ids, and a close for every weekday from 14 Oct 2024 to 9 Oct 2026 (about 520 days) written in one statement
        await ExecuteAsync(
            """
            INSERT INTO market.instruments (id, canonical_symbol, exchange, exchange_segment, type, name, display_name, isin, series, lot_size, underlying_symbol, fno_eligible, first_seen_at, last_seen_at)
            SELECT gen_random_uuid(), 'NSE:PERF' || lpad(n::text, 3, '0'), 'NSE', 'NSE_EQ', 'share', 'Perf ' || n, 'Perf ' || n, 'INE9' || lpad(n::text, 8, '0'), 'EQ', 1, 'PERF' || lpad(n::text, 3, '0'), false, now(), now()
            FROM generate_series(1, @count) n
            """,
            ("count", Holdings));
        await ExecuteAsync(
            "INSERT INTO market.instrument_broker_ids (instrument_id, broker, broker_id, segment) SELECT id, 'dhan', (7000 + substr(canonical_symbol, 9)::int)::text, 'NSE_EQ' FROM market.instruments WHERE canonical_symbol LIKE 'NSE:PERF%'");
        await ExecuteAsync(
            """
            INSERT INTO market.bars_agg (instrument_id, timeframe, ts, open, high, low, close, volume, bars, complete)
            SELECT i.id, '1D', (d::timestamp AT TIME ZONE 'Asia/Kolkata'), c, c, c, c, 1000, 375, true
            FROM market.instruments i
            CROSS JOIN LATERAL (SELECT d, 100 + extract(doy FROM d)::double precision / 10 + substr(i.canonical_symbol, 9)::int AS c
                                FROM generate_series(date '2024-10-14', date '2026-10-09', interval '1 day') d WHERE extract(isodow FROM d) < 6) x
            WHERE i.canonical_symbol LIKE 'NSE:PERF%'
            """);
        var account = await ConnectedAccountAsync();
        for (var i = 1; i <= Holdings; i++)
        {
            Dhan.Holdings.Add(Holding("PERF" + i.ToString("000", CultureInfo.InvariantCulture), 10 + i, 100m, 150m, (7000 + i).ToString(CultureInfo.InvariantCulture), "INE9" + i.ToString("00000000", CultureInfo.InvariantCulture)));
        }

        await ImportAllAsync(account.Id, "2024-10-14");
        InvalidateHistory();

        var started = Stopwatch.GetTimestamp();
        var history = await HistoryAsync();
        var elapsed = Stopwatch.GetElapsedTime(started);
        var reads = await BarReadsDuringAsync(async () =>
        {
            InvalidateHistory();
            await HistoryAsync();
        });

        history.Coverage.Holdings.ShouldBe(Holdings);
        history.Coverage.Covered.ShouldBe(Holdings);
        history.Points.Count.ShouldBeGreaterThan(500);
        history.Points[^1].Date.ShouldBe(Today);
        reads.ShouldBe(1);
        elapsed.ShouldBeLessThan(GenerousBound);
    }
}
