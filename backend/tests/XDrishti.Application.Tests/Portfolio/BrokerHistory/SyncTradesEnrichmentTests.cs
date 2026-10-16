using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.BrokerHistory.BrokerScenario;
using static XDrishti.Application.Tests.Portfolio.PortfolioFixture;

namespace XDrishti.Application.Tests.Portfolio.BrokerHistory;

/// <summary>
/// Trades stored by the first P2-13 build have charges 0 and no ISIN; reading them again completes those fields and nothing else (ADR 0026): never the identity, quantity, price, date or side, never a trade from a file, never a voided one, never a value that is there.
/// </summary>
public sealed class SyncTradesEnrichmentTests
{
    private const string Isin = "INE000A01011";

    private static async Task<(BrokerScenario Scenario, List<(Guid Id, string ExternalId, DateOnly Date, long Qty, decimal Price, TradeSide Side)> Before)> StoredByTheEarlierBuildAsync()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Trades.AddRange(BaseTrades()); // no ISIN, charges 0: what the earlier build stored
        await s.SyncOkAsync();
        var before = s.BrokerTrades().Select(t => (t.Id, t.ExternalId, t.TradeDate, t.Qty, t.Price, t.Side)).ToList();
        s.Dhan.Trades.Clear();
        s.Dhan.Trades.AddRange(
        [
            Trade("T1", new DateOnly(2026, 9, 2), TradeSide.Buy, 10, 90m, isin: Isin, charges: 12.34m),
            Trade("T2", new DateOnly(2026, 9, 15), TradeSide.Buy, 5, 110m, isin: Isin, charges: 7.5m),
            Trade("T3", new DateOnly(2026, 9, 20), TradeSide.Sell, 3, 130m, isin: Isin, charges: 9m),
        ]);
        return (s, before);
    }

    [Fact]
    public async Task Reading_the_days_again_completes_the_isin_and_charges_of_the_stored_trades_and_counts_them()
    {
        var (s, before) = await StoredByTheEarlierBuildAsync();
        s.BrokerTrades().ShouldAllBe(t => t.Isin == null && t.Charges == 0m);

        var view = await s.SyncOkAsync();

        (view.Trades.Counts.Stored, view.Trades.Counts.Duplicates, view.Trades.Counts.Enriched).ShouldBe((0, 3, 3));
        s.BrokerTrades().Select(t => (t.ExternalId, t.Isin, t.Charges)).OrderBy(t => t.ExternalId).ShouldBe(
        [
            ("t:20260902:T1", Isin, 12.34m),
            ("t:20260915:T2", Isin, 7.5m),
            ("t:20260920:T3", Isin, 9m),
        ]);
        s.BrokerTrades().Select(t => (t.Id, t.ExternalId, t.TradeDate, t.Qty, t.Price, t.Side)).ShouldBe(before, ignoreOrder: true);
    }

    [Fact]
    public async Task A_second_read_completes_nothing_more()
    {
        var (s, _) = await StoredByTheEarlierBuildAsync();
        await s.SyncOkAsync();

        var again = await s.SyncOkAsync();

        (again.Trades.Counts.Duplicates, again.Trades.Counts.Enriched).ShouldBe((3, 0));
    }

    [Fact]
    public async Task A_value_that_is_there_is_never_overwritten_and_a_read_without_charges_adds_none()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Trades.Add(Trade("T1", new DateOnly(2026, 9, 2), TradeSide.Buy, 10, 90m, isin: Isin, charges: 5m));
        s.Dhan.Trades.Add(Trade("T2", new DateOnly(2026, 9, 15), TradeSide.Buy, 5, 110m));
        await s.SyncOkAsync();
        s.Dhan.Trades.Clear();
        s.Dhan.Trades.Add(Trade("T1", new DateOnly(2026, 9, 2), TradeSide.Buy, 10, 90m, isin: "INE999Z01019", charges: 99m));
        s.Dhan.Trades.Add(Trade("T2", new DateOnly(2026, 9, 15), TradeSide.Buy, 5, 110m, charges: 0m));

        var view = await s.SyncOkAsync();

        view.Trades.Counts.Enriched.ShouldBe(0);
        s.BrokerTrades().Single(t => t.ExternalId.EndsWith("T1", StringComparison.Ordinal)).ShouldSatisfyAllConditions(t => t.Isin.ShouldBe(Isin), t => t.Charges.ShouldBe(5m));
        s.BrokerTrades().Single(t => t.ExternalId.EndsWith("T2", StringComparison.Ordinal)).Charges.ShouldBe(0m);
    }

    [Fact]
    public async Task A_trade_from_a_file_with_the_same_identity_is_not_touched()
    {
        var s = await BrokerScenario.StartAsync();
        var csv = Transaction.Create(
            s.Account.Id, null, null, "EXMPL", "NSE", TradeSide.Buy, new DateOnly(2026, 9, 2), null, 10, 90m, 0m, TradeSource.Csv, null, 1, "t:20260902:T1", Actor, s.Fx.Clock.GetUtcNow());
        s.Fx.Trades.Trades.Add(csv);
        s.Dhan.Trades.Add(Trade("T1", new DateOnly(2026, 9, 2), TradeSide.Buy, 10, 90m, isin: Isin, charges: 12m));

        var view = await s.SyncOkAsync();

        (view.Trades.Counts.Stored, view.Trades.Counts.Duplicates, view.Trades.Counts.Enriched).ShouldBe((0, 1, 0));
        (csv.Isin, csv.Charges).ShouldBe((null, 0m));
    }

    [Fact]
    public async Task A_voided_trade_is_not_touched_and_its_identity_is_read_afresh()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Trades.Add(Trade("T1", new DateOnly(2026, 9, 2), TradeSide.Buy, 10, 90m));
        await s.SyncOkAsync();
        var voided = s.BrokerTrades().Single();
        voided.Void(Actor, s.Fx.Clock.GetUtcNow()).ShouldBeTrue();
        s.Dhan.Trades.Clear();
        s.Dhan.Trades.Add(Trade("T1", new DateOnly(2026, 9, 2), TradeSide.Buy, 10, 90m, isin: Isin, charges: 12m));

        var view = await s.SyncOkAsync();

        (view.Trades.Counts.Stored, view.Trades.Counts.Enriched).ShouldBe((1, 0));
        (voided.Isin, voided.Charges).ShouldBe((null, 0m));
    }

    [Fact]
    public async Task The_audit_text_gives_the_count_and_no_trade_content()
    {
        var (s, _) = await StoredByTheEarlierBuildAsync();

        await s.SyncOkAsync();

        var detail = s.Fx.Events(AccountEventType.BrokerHistorySynced).Last(e => e.Detail!.StartsWith("Broker trades", StringComparison.Ordinal)).Detail!;
        detail.ShouldContain("3 already stored (3 completed with an ISIN, charges or ticker)");
        detail.ShouldNotContain("12.34");
        detail.ShouldNotContain(Isin);
        detail.ShouldNotContain("T1");
    }
}
