using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.BrokerHistory;

/// <summary>
/// Trades stored by the first P2-13 build have charges 0 and no ISIN, and are immutable; a later read completes them (P2-14, ADR 0026) on the real database and with the runtime role: only the missing ISIN and the zero charges, nothing else of the
/// trade, nothing of a trade that is not from the broker.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class BrokerHistoryEnrichmentTests(ApiFactory factory) : BrokerHistoryTestBase(factory)
{
    private const string Identities = "('t:20261005:T1', 't:20261006:T2', 't:20261008:T3')";

    [Fact]
    public async Task A_later_read_completes_the_isin_and_charges_of_stored_broker_trades_and_nothing_else_and_not_the_trades_of_files()
    {
        var held = await HeldAsync();
        DhanReports(isin: null, brokerage: 0m);
        await SyncAndWaitAsync(held.Account.Id);
        (await TransactionsAsync("source = 'broker' AND isin IS NULL AND charges = 0")).ShouldBe(3);
        await ExecuteAsync("UPDATE portfolio.transactions SET source = 'csv', run_id = NULL WHERE external_id = 't:20261008:T3'"); // the same trade, imported from a tradebook file
        DhanReports(isin: TradesApi.Isin, brokerage: 12.5m);

        var (_, status) = await SyncAndWaitAsync(held.Account.Id);

        var counts = status.Trades.LastRun!.Counts;
        (counts.Stored, counts.Duplicates, counts.Enriched).ShouldBe((0, 3, 2));
        (await TransactionsAsync($"source = 'broker' AND isin = '{TradesApi.Isin}' AND charges = 13.0 AND external_id IN ('t:20261005:T1', 't:20261006:T2')")).ShouldBe(2); // 12.5 brokerage + 0.5 STT
        (await TransactionsAsync("source = 'csv' AND isin IS NULL AND charges = 0 AND external_id = 't:20261008:T3'")).ShouldBe(1);
        (await TransactionsAsync()).ShouldBe(3);
        (await TransactionsAsync(
            $"external_id IN {Identities} AND ((external_id = 't:20261005:T1' AND side = 'buy' AND qty = 10 AND price = 90 AND trade_date = '2026-10-05')"
            + " OR (external_id = 't:20261006:T2' AND side = 'buy' AND qty = 5 AND price = 110 AND trade_date = '2026-10-06')"
            + " OR (external_id = 't:20261008:T3' AND side = 'sell' AND qty = 3 AND price = 130 AND trade_date = '2026-10-08'))")).ShouldBe(3);
        (await EventsAsync("BrokerHistorySynced")).ShouldBeGreaterThan(0);
        (await CountAsync("SELECT count(*) FROM accounts.account_events WHERE event = 'BrokerHistorySynced' AND detail LIKE '%2 completed%' AND detail NOT LIKE '%13%' AND detail NOT LIKE '%T1%'")).ShouldBe(1);
    }

    [Fact]
    public async Task A_read_that_adds_nothing_completes_nothing_and_a_third_read_finds_it_all_complete()
    {
        var held = await HeldAsync();
        DhanReports(isin: null, brokerage: 0m);
        await SyncAndWaitAsync(held.Account.Id);

        var unchanged = await SyncAndWaitAsync(held.Account.Id);
        DhanReports(isin: TradesApi.Isin, brokerage: 12.5m);
        var completed = await SyncAndWaitAsync(held.Account.Id);
        var again = await SyncAndWaitAsync(held.Account.Id);

        unchanged.Status.Trades.LastRun!.Counts.Enriched.ShouldBe(0);
        completed.Status.Trades.LastRun!.Counts.Enriched.ShouldBe(3);
        again.Status.Trades.LastRun!.Counts.Enriched.ShouldBe(0);
    }

    private void DhanReports(string? isin, decimal brokerage)
    {
        Dhan.Trades.Clear();
        Dhan.Trades.Add(new FakeBrokers.FakeTrade("T1", "EXMPL", "BUY", 10, 90m, D(5), Morning, Isin: isin, Brokerage: brokerage));
        Dhan.Trades.Add(new FakeBrokers.FakeTrade("T2", "EXMPL", "BUY", 5, 110m, D(6), Morning, Isin: isin, Brokerage: brokerage));
        Dhan.Trades.Add(new FakeBrokers.FakeTrade("T3", "EXMPL", "SELL", 3, 130m, D(8), Morning, Isin: isin, Brokerage: brokerage));
    }
}
