using System.Diagnostics;
using System.Globalization;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.ActionCenter;

/// <summary>
/// The Action center over 500 holdings (P2-09: under 300 ms in the spec; measured on a laptop it is far below that, so after a warm-up request the guard allows 1 s: tight enough to catch a repeated XIRR solve per rule, loose enough not to flake) and the reads it makes: still one of each,
/// and one of the index bars for the lagging rule. Clock: Monday 2026-10-12 10:00 India time.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ActionCenterPerformanceTests(ApiFactory factory) : ActionCenterTestBase(factory)
{
    private const int Holdings = 500;

    private static readonly TimeSpan GenerousBound = TimeSpan.FromSeconds(1);

    [Fact]
    public async Task Five_hundred_holdings_with_the_lagging_rule_are_one_read_each_and_fast_enough()
    {
        await IndexAsync("NSE:NIFTY", "Nifty 50", (new DateOnly(2024, 10, 11), 10000.0), (new DateOnly(2026, 10, 9), 20000.0));
        await ExecuteAsync(
            """
            INSERT INTO market.instruments (id, canonical_symbol, exchange, exchange_segment, type, name, display_name, isin, series, lot_size, underlying_symbol, fno_eligible, sector, first_seen_at, last_seen_at)
            SELECT gen_random_uuid(), 'NSE:PERF' || lpad(n::text, 3, '0'), 'NSE', 'NSE_EQ', 'share', 'Perf ' || n, 'Perf ' || n, 'INE9' || lpad(n::text, 8, '0'), 'EQ', 1, 'PERF' || lpad(n::text, 3, '0'), false, 'Sector ' || (n % 12), now(), now()
            FROM generate_series(1, @count) n
            """,
            ("count", Holdings));
        await ExecuteAsync("INSERT INTO market.instrument_broker_ids (instrument_id, broker, broker_id, segment) SELECT id, 'dhan', (7000 + substr(canonical_symbol, 9)::int)::text, 'NSE_EQ' FROM market.instruments WHERE canonical_symbol LIKE 'NSE:PERF%'");
        Dhan.Holdings.Clear();
        for (var i = 1; i <= Holdings; i++)
        {
            Dhan.Holdings.Add(Holding("PERF" + i.ToString("000", CultureInfo.InvariantCulture), 10 + i, 100m, 60m + (i % 90), (7000 + i).ToString(CultureInfo.InvariantCulture), "INE9" + i.ToString("00000000", CultureInfo.InvariantCulture)));
        }

        var account = await ConnectedAccountAsync("Example Two", "1000000002");
        await ImportAllAsync(account.Id, "2024-10-14");
        await ActionsAsync();

        var started = Stopwatch.GetTimestamp();
        var list = await ActionsAsync();
        var elapsed = Stopwatch.GetElapsedTime(started);
        var reads = await Client.StatementsDuringAsync(async () => await ActionsAsync());

        list.Items.Count.ShouldBeGreaterThan(Holdings / 2);
        list.Items.Select(i => i.Rule).Distinct().ShouldContain("lagging_nifty");
        reads.Count(s => s.Contains("portfolio.holdings ", StringComparison.Ordinal)).ShouldBe(1);
        reads.Count(s => s.Contains("portfolio.holding_lots", StringComparison.Ordinal)).ShouldBe(1);
        reads.Count(s => s.Contains("market.bars_agg", StringComparison.Ordinal)).ShouldBe(1);
        reads.Count(s => s.Contains("market.basket_members", StringComparison.Ordinal)).ShouldBe(1);
        elapsed.ShouldBeLessThan(GenerousBound);
    }
}
