using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Portfolio;
using XDrishti.Application.Tests.Portfolio.Trades;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.BrokerHistory.BrokerScenario;
using static XDrishti.Application.Tests.Portfolio.PortfolioFixture;
using static XDrishti.Application.Tests.Portfolio.Trades.TradeScenario;

namespace XDrishti.Application.Tests.Portfolio.BrokerHistory;

/// <summary>The automatic rebuild of lots after a trade sync and its three safeguards (ADR 0025, P2-13 criteria 13-15): only <c>ok</c>, never over the owner's own dates, skipped when the holding changed meanwhile.</summary>
public sealed class BrokerAutoRebuildTests
{
    [Fact]
    public async Task A_holding_whose_result_is_ok_is_rebuilt_from_the_real_dates_without_a_preview()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Trades.AddRange(BaseTrades());

        var view = await s.SyncOkAsync();

        var counts = view.Trades.Counts;
        (counts.Rebuilt, counts.Unchanged, counts.NeedsReview, counts.NotRebuiltTotal).ShouldBe((1, 0, 0, 0));
        s.Holding.Lots.Count.ShouldBe(4); // the opening lot, two buys, one of them split by the sale
        s.Holding.Lots.Where(l => l.IsOpen).Sum(l => l.Qty).ShouldBe(30);
        s.Holding.Lots.Count(l => l.Source == LotSource.Trade).ShouldBeGreaterThan(0);
        s.Holding.Lots.ShouldContain(l => l.AcquiredOn == new DateOnly(2026, 9, 2));
    }

    [Fact]
    public async Task It_uses_the_same_lots_as_the_owners_rebuild_of_the_same_trades()
    {
        var automatic = await BrokerScenario.StartAsync();
        automatic.Dhan.Trades.AddRange(BaseTrades());
        await automatic.SyncOkAsync();
        var manual = await TradeScenario.StartAsync();
        await manual.ImportOkAsync(Csv("2026-09-02,EXMPL,INE000A01011,buy,10,90,T1", "2026-09-15,EXMPL,INE000A01011,buy,5,110,T2", "2026-09-20,EXMPL,INE000A01011,sell,3,130,T3"));

        Shape(automatic.Holding).ShouldBe(Shape(manual.Holding));
    }

    [Fact]
    public async Task Every_rebuild_is_audited_with_counts_only_and_the_trigger()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Trades.AddRange(BaseTrades());

        await s.SyncOkAsync();

        var rebuilt = s.Fx.Events(AccountEventType.LotsRebuilt).ShouldHaveSingleItem();
        rebuilt.Detail.ShouldBe("Lots rebuilt from trades: 1 holding, 1 lots replaced by 4 (0 hand-dated, 0 added by syncs), trigger broker_sync");
        rebuilt.Actor.ShouldBe(BrokerHistoryPipelineActor);
        (rebuilt.Detail!.Contains("EXMPL", StringComparison.Ordinal) || rebuilt.Detail.Contains("130", StringComparison.Ordinal)).ShouldBeFalse();
    }

    private const string BrokerHistoryPipelineActor = "system:broker-history";

    [Fact]
    public async Task A_second_run_that_stores_nothing_and_has_nothing_to_retry_does_not_look_at_the_holdings_again()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Trades.AddRange(BaseTrades());
        await s.SyncOkAsync();
        var lots = Shape(s.Holding);
        s.Fx.Trades.BrokerSecurityReads.ShouldBe(1);

        var again = await s.SyncOkAsync();

        (again.Trades.Counts.Stored, again.Trades.Counts.Rebuilt, again.Trades.Counts.Unchanged).ShouldBe((0, 0, 0));
        s.Fx.Trades.BrokerSecurityReads.ShouldBe(1); // the rebuild was skipped
        s.Fx.Trades.WholeHistoryReads.ShouldBe(0); // and the account's whole history is never read for it
        Shape(s.Holding).ShouldBe(lots);
        s.Fx.Events(AccountEventType.LotsRebuilt).Count.ShouldBe(1);
    }

    /// <summary>Trades of LATE are stored while the account holds no such holding (no_holding); a quiet run after that leaves the same state.</summary>
    private static async Task<BrokerScenario> LateTradesStoredWithoutHoldingAsync()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Trades.Add(Trade("L1", new DateOnly(2026, 9, 2), TradeSide.Buy, 5, 50m, symbol: "LATE"));
        (await s.SyncOkAsync()).Trades.Counts.NotRebuilt.ShouldBe([new BrokerNotRebuilt("LATE", "no_holding")]);
        return s;
    }

    [Fact]
    public async Task A_holding_added_by_a_holdings_sync_after_its_trades_were_stored_is_rebuilt_by_the_next_quiet_run()
    {
        var s = await LateTradesStoredWithoutHoldingAsync();
        s.Fx.Clock.Advance(TimeSpan.FromMinutes(10));
        s.Fx.Dhan.Rows.Add(Row("LATE", 5, 50m, 55m, "2002", "INE000A02022")); // the owner's next holdings sync lists it
        Ok(await s.Fx.ImportAsync(s.Account.Id, new ImportInput(null, null, All: true)));
        s.Fx.Clock.Advance(TimeSpan.FromMinutes(10));

        var quiet = await s.SyncOkAsync(defaultFrom: true);

        (quiet.Trades.Counts.Stored, quiet.Trades.Counts.Rebuilt).ShouldBe((0, 1));
        s.Fx.HoldingOf("LATE").Lots.ShouldContain(l => l.Source == LotSource.Trade && l.AcquiredOn == new DateOnly(2026, 9, 2));
    }

    [Fact]
    public async Task The_holding_is_not_missed_when_a_quiet_run_that_skipped_the_rebuild_came_in_between()
    {
        var s = await LateTradesStoredWithoutHoldingAsync();
        s.Fx.Clock.Advance(TimeSpan.FromMinutes(10));
        (await s.SyncOkAsync(defaultFrom: true)).Trades.Counts.NotRebuiltTotal.ShouldBe(0); // nothing changed: the rebuild was skipped, so this run reports nothing
        s.Fx.Clock.Advance(TimeSpan.FromMinutes(10));
        s.Fx.Dhan.Rows.Add(Row("LATE", 5, 50m, 55m, "2002", "INE000A02022"));
        Ok(await s.Fx.ImportAsync(s.Account.Id, new ImportInput(null, null, All: true)));
        s.Fx.Clock.Advance(TimeSpan.FromMinutes(10));

        var quiet = await s.SyncOkAsync(defaultFrom: true);

        quiet.Trades.Counts.Rebuilt.ShouldBe(1);
    }

    [Fact]
    public async Task A_quiet_run_after_one_that_left_only_no_holding_behind_still_skips_the_rebuild_when_no_holding_appeared()
    {
        var s = await LateTradesStoredWithoutHoldingAsync();
        s.Fx.Trades.BrokerSecurityReads.ShouldBe(1);
        s.Fx.Clock.Advance(TimeSpan.FromMinutes(10));
        s.Fx.Dhan.Rows.Clear(); // a holdings sync that lists the same (here: nothing new) must not count as a new holding
        s.Fx.Dhan.Rows.Add(Row("EXMPL", 30, 100m, 125m, "1001", Isin));
        Ok(await s.Fx.ImportAsync(s.Account.Id, new ImportInput(null, null, All: true)));
        s.Fx.Clock.Advance(TimeSpan.FromMinutes(10));

        var quiet = await s.SyncOkAsync(defaultFrom: true);

        (quiet.Trades.Counts.Stored, quiet.Trades.Counts.Rebuilt).ShouldBe((0, 0));
        s.Fx.Trades.BrokerSecurityReads.ShouldBe(1);
    }

    [Fact]
    public async Task A_run_that_stores_nothing_still_rebuilds_when_the_run_before_failed_after_storing_trades()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Trades.AddRange(BaseTrades());
        s.Dhan.TradeFailure = (from, _, _) => from == new DateOnly(2026, 10, 1) ? new BrokerFailure(BrokerFailureKind.Unavailable) : null;
        var failed = await s.SyncOkAsync(); // stores September, fails on October: no rebuild
        failed.Trades.Status.ShouldBe("failed");
        s.Holding.Lots.ShouldAllBe(l => l.Source != LotSource.Trade);
        s.Dhan.TradeFailure = null;

        var next = await s.SyncOkAsync(defaultFrom: true);

        (next.Trades.Counts.Stored, next.Trades.Counts.Rebuilt).ShouldBe((0, 1));
        s.Holding.Lots.ShouldContain(l => l.Source == LotSource.Trade);
    }

    [Fact]
    public async Task A_holding_left_for_review_is_looked_at_again_by_a_run_that_stores_nothing()
    {
        var s = await BrokerScenario.StartAsync();
        s.Holding.ChangeLotDate(s.Holding.Lots.Single().Id, new DateOnly(2023, 2, 2), s.Fx.Today).IsSuccess.ShouldBeTrue();
        s.Dhan.Trades.AddRange(BaseTrades());
        await s.SyncOkAsync();

        var again = await s.SyncOkAsync();

        (again.Trades.Counts.Stored, again.Trades.Counts.NeedsReview).ShouldBe((0, 1));
        s.Fx.Trades.BrokerSecurityReads.ShouldBe(2);
    }

    [Fact]
    public async Task A_quantity_that_the_trades_do_not_fit_is_left_alone_and_counted()
    {
        var s = await BrokerScenario.StartAsync(qty: 10);
        s.Dhan.Trades.Add(Trade("T1", new DateOnly(2026, 9, 2), TradeSide.Buy, 15, 90m)); // more than the broker holds
        var lots = Shape(s.Holding);

        var counts = (await s.SyncOkAsync()).Trades.Counts;

        (counts.Rebuilt, counts.NotRebuiltTotal).ShouldBe((0, 1));
        counts.NotRebuilt.ShouldBe([new BrokerNotRebuilt("EXMPL", "quantity_mismatch", "negative_opening", s.Holding.Id)]);
        Shape(s.Holding).ShouldBe(lots);
        s.Fx.Events(AccountEventType.LotsRebuilt).ShouldBeEmpty();
    }

    [Fact]
    public async Task Trades_of_a_security_the_account_does_not_hold_are_stored_and_counted_as_no_holding()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Trades.Add(Trade("T1", new DateOnly(2026, 9, 2), TradeSide.Buy, 5, 50m, symbol: "GONE"));
        s.Dhan.Trades.Add(Trade("T2", new DateOnly(2026, 9, 3), TradeSide.Sell, 5, 55m, symbol: "GONE"));

        var counts = (await s.SyncOkAsync()).Trades.Counts;

        s.BrokerTrades().Count.ShouldBe(2);
        counts.NotRebuilt.ShouldBe([new BrokerNotRebuilt("GONE", "no_holding")]);
        counts.Rebuilt.ShouldBe(0);
    }

    [Fact]
    public async Task A_holding_with_a_hand_dated_lot_is_not_rebuilt_and_is_reported_for_review()
    {
        var s = await BrokerScenario.StartAsync();
        s.Holding.ChangeLotDate(s.Holding.Lots.Single().Id, new DateOnly(2023, 2, 2), s.Fx.Today).IsSuccess.ShouldBeTrue();
        s.Dhan.Trades.AddRange(BaseTrades());
        var lots = Shape(s.Holding);

        var view = await s.SyncOkAsync();

        var counts = view.Trades.Counts;
        (counts.Rebuilt, counts.NeedsReview, counts.NotRebuiltTotal).ShouldBe((0, 1, 1));
        counts.NotRebuilt.ShouldBe([new BrokerNotRebuilt("EXMPL", "needs_review", null, s.Holding.Id)]);
        Shape(s.Holding).ShouldBe(lots);
        s.BrokerTrades().Count.ShouldBe(3); // the trades are stored all the same
        s.Fx.Events(AccountEventType.LotsRebuilt).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_owners_own_preview_then_rebuild_still_works_for_a_holding_that_needs_review()
    {
        var s = await BrokerScenario.StartAsync();
        s.Holding.ChangeLotDate(s.Holding.Lots.Single().Id, new DateOnly(2023, 2, 2), s.Fx.Today).IsSuccess.ShouldBeTrue();
        s.Dhan.Trades.AddRange(BaseTrades());
        await s.SyncOkAsync();

        var dry = Ok(await s.Tradebook.RebuildAsync(dryRun: true));
        var real = Ok(await s.Tradebook.RebuildAsync(dryRun: false));

        (dry.Rebuild.Status, dry.Rebuild.HandDatedLotsReplaced).ShouldBe(("ok", 1));
        real.Applied.ShouldBeTrue();
        s.Holding.Lots.Count(l => l.Source == LotSource.Trade).ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task A_holding_that_changed_meanwhile_is_skipped_and_the_next_run_rebuilds_it()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Trades.AddRange(BaseTrades());
        s.Fx.Data.RacedRuns = 1; // a sync or a quote saved the holding between the read and the save
        var lots = Shape(s.Holding);

        var raced = await s.SyncOkAsync();

        (raced.Trades.Counts.Rebuilt, raced.Trades.Status).ShouldBe((0, "succeeded"));
        raced.Trades.Counts.NotRebuilt.ShouldBe([new BrokerNotRebuilt("EXMPL", "changed_meanwhile", null, s.Holding.Id)]);
        Shape(s.Holding).ShouldBe(lots);
        s.Fx.Data.OnceRuns.ShouldBe(1); // it was not retried

        var next = await s.SyncOkAsync();

        next.Trades.Counts.Rebuilt.ShouldBe(1);
        s.Holding.Lots.Count(l => l.Source == LotSource.Trade).ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Nothing_is_rebuilt_when_a_window_failed_because_the_history_would_have_a_hole()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Trades.AddRange(BaseTrades());
        s.Dhan.TradeFailure = (from, _, _) => from == new DateOnly(2026, 10, 1) ? new BrokerFailure(BrokerFailureKind.Unavailable) : null;
        var lots = Shape(s.Holding);

        await s.SyncOkAsync();

        Shape(s.Holding).ShouldBe(lots);
        s.Fx.Events(AccountEventType.LotsRebuilt).ShouldBeEmpty();
    }

    [Fact]
    public async Task Nothing_is_rebuilt_when_the_run_starts_after_the_watermark_when_the_start_check_is_bypassed_defensive_guard()
    {
        // This test bypasses the access check on purpose (BrokerHistoryAccess.CheckFromAsync refuses such a start date, so no use case gets here; BrokerWatermarkGapTests cover that): it only proves the pipeline's own defensive guard, because a history with a hole in the middle would put the missing shares into the opening lot.
        var s = await BrokerScenario.StartAsync();
        s.Dhan.TradeFailure = (from, _, _) => from == new DateOnly(2026, 10, 1) ? new BrokerFailure(BrokerFailureKind.Unavailable) : null;
        await s.SyncOkAsync(); // reads September, fails on October: the watermark is 2026-09-30
        s.Dhan.TradeFailure = null;
        s.Dhan.Trades.Add(Trade("T1", new DateOnly(2026, 10, 6), TradeSide.Buy, 1, 140m));
        var lots = Shape(s.Holding);
        var pipeline = s.Pipeline();
        var token = (await s.Access().TokenAsync(s.Account, CancellationToken.None)).Value;
        var begun = (await pipeline.BeginAsync(s.Account, BrokerSyncKind.Trades, scheduled: false, new DateOnly(2026, 10, 5), Actor, CancellationToken.None)).Value; // leaves a hole from the 1st to the 4th

        var view = await pipeline.CarryOutAsync(s.Account, token, begun, new DateOnly(2026, 10, 5), scheduled: false, Actor, CancellationToken.None);

        (view.Trades.Counts.Stored, view.Trades.Counts.Rebuilt).ShouldBe((1, 0));
        s.Data.Watermarks[(s.Account.Id, BrokerSyncKind.Trades)].ShouldBe(new DateOnly(2026, 9, 30)); // and the watermark did not jump over the hole
        Shape(s.Holding).ShouldBe(lots);
    }

    [Fact]
    public async Task A_start_date_further_back_than_the_watermark_is_contiguous_and_rebuilds()
    {
        var s = await BrokerScenario.StartAsync();
        await s.SyncOkAsync(from: new DateOnly(2026, 10, 1));
        s.Dhan.Trades.AddRange(BaseTrades());

        var view = await s.SyncOkAsync(from: new DateOnly(2026, 9, 1));

        view.Trades.Counts.Rebuilt.ShouldBe(1);
    }

    [Fact]
    public async Task Only_holdings_that_have_broker_trades_are_considered()
    {
        var s = await BrokerScenario.StartAsync();
        await s.Tradebook.ImportOkAsync(Csv("2024-03-05,EXMPL,INE000A01011,buy,10,90,T1"), rebuild: false);

        var counts = (await s.SyncOkAsync()).Trades.Counts;

        (counts.Rebuilt, counts.Unchanged, counts.NotRebuiltTotal).ShouldBe((0, 0, 0));
        s.Holding.Lots.ShouldAllBe(l => l.Source != LotSource.Trade);
    }

    [Fact]
    public async Task The_trades_of_a_closed_holding_are_not_applied()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Trades.AddRange(BaseTrades());
        s.Holding.CloseMissing(s.Fx.Today, s.Fx.Clock.GetUtcNow()).ShouldBeTrue();

        var counts = (await s.SyncOkAsync()).Trades.Counts;

        counts.Rebuilt.ShouldBe(0);
        counts.NotRebuilt.ShouldBe([new BrokerNotRebuilt("EXMPL", "no_holding")]);
    }
}
