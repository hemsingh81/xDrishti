using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Portfolio.Trades;
using XDrishti.Application.Tests.Portfolio.Trades;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.BrokerHistory.BrokerScenario;
using static XDrishti.Application.Tests.Portfolio.PortfolioFixture;

namespace XDrishti.Application.Tests.Portfolio.BrokerHistory;

/// <summary>Reading an account's trades from Dhan (ADR 0025, P2-13 criteria 1-8, 10-12): the backfill in windows, the watermark, partial failures, dedupe, voided trades, the refusal to overlap other syncs.</summary>
public sealed class SyncTradesTests
{
    // ---- the backfill and the watermark ----

    [Fact]
    public async Task The_first_run_without_a_start_date_reads_from_the_first_import_date_in_windows_of_thirty_days()
    {
        var s = await BrokerScenario.StartAsync();

        var view = await s.SyncOkAsync(defaultFrom: true);

        var windows = s.Dhan.TradeCalls.Select(c => (c.From, c.To)).Distinct().ToList();
        windows.Count.ShouldBe(22); // 2025-01-01 to 2026-10-10, 648 dates
        windows[0].ShouldBe((new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 30)));
        windows[^1].ShouldBe((new DateOnly(2026, 9, 23), new DateOnly(2026, 10, 10)));
        (view.Trades.Counts.WindowsDone, view.Trades.Counts.WindowsTotal).ShouldBe((22, 22));
        (view.Trades.From, view.Trades.To, view.Trades.Status).ShouldBe((new DateOnly(2025, 1, 1), new DateOnly(2026, 10, 10), "succeeded"));
        s.Data.Watermarks[(s.Account.Id, BrokerSyncKind.Trades)].ShouldBe(new DateOnly(2026, 10, 10));
    }

    [Fact]
    public async Task The_watermark_moves_after_every_window_to_the_end_of_that_window()
    {
        var s = await BrokerScenario.StartAsync();

        await s.SyncOkAsync();

        s.Data.WatermarkMoves.Where(m => m.Kind == BrokerSyncKind.Trades).Select(m => m.Date).ShouldBe([new DateOnly(2026, 9, 30), new DateOnly(2026, 10, 10)]);
    }

    [Fact]
    public async Task The_next_run_starts_at_the_watermark_and_stores_nothing_twice()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Trades.AddRange(BaseTrades());
        await s.SyncOkAsync();
        s.Dhan.TradeCalls.Clear();
        s.Dhan.Trades.Add(Trade("T4", new DateOnly(2026, 10, 9), TradeSide.Buy, 1, 120m));

        var view = await s.SyncOkAsync(defaultFrom: true);

        s.Dhan.TradeCalls.Select(c => (c.From, c.To)).Distinct().ShouldBe([(new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 10))]);
        (view.Trades.From, view.Trades.Counts.Stored).ShouldBe((new DateOnly(2026, 10, 10), 0));
        s.BrokerTrades().Count.ShouldBe(3); // T4 is before the watermark: only an explicit start date reads it
    }

    [Fact]
    public async Task Reading_the_same_days_twice_stores_nothing_new()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Trades.AddRange(BaseTrades());
        var first = await s.SyncOkAsync();

        var second = await s.SyncOkAsync();

        (first.Trades.Counts.Stored, first.Trades.Counts.Duplicates).ShouldBe((3, 0));
        (second.Trades.Counts.Stored, second.Trades.Counts.Duplicates, second.Trades.Counts.Read).ShouldBe((0, 3, 3));
        s.BrokerTrades().Count.ShouldBe(3);
    }

    [Fact]
    public async Task A_start_date_after_today_or_before_1990_is_refused_with_the_field()
    {
        var s = await BrokerScenario.StartAsync();

        var future = await s.SyncAsync(from: new DateOnly(2026, 10, 11));
        var ancient = await s.SyncAsync(from: new DateOnly(1989, 12, 31));

        (future.Error.Code, ancient.Error.Code).ShouldBe(("portfolio.invalid", "portfolio.invalid"));
        FieldsOf(future).ShouldBe(["from"]);
        s.Data.Runs.ShouldBeEmpty();
        s.Dhan.TradeCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_start_date_of_today_reads_the_one_day()
    {
        var s = await BrokerScenario.StartAsync();

        var view = await s.SyncOkAsync(from: new DateOnly(2026, 10, 10));

        view.Trades.Counts.WindowsTotal.ShouldBe(1);
    }

    // ---- what is stored ----

    [Fact]
    public async Task A_trade_is_stored_as_a_broker_transaction_with_the_run_and_the_identity_of_the_csv_reader()
    {
        var s = await BrokerScenario.StartAsync();
        var instrument = new XDrishti.Application.Abstractions.Portfolio.TradeInstrument(Guid.NewGuid(), "EXMPL", TradeScenario.Isin, "share");
        s.Fx.Trades.Instruments.Add(instrument);
        s.Dhan.Trades.Add(Trade("T1", new DateOnly(2026, 9, 2), TradeSide.Buy, 10, 90.5m, symbol: "EXMPL", time: new TimeOnly(11, 20, 6)));

        var view = await s.SyncOkAsync();

        var trade = s.BrokerTrades().Single();
        (trade.Source, trade.RunId, trade.AccountId, trade.ExternalId).ShouldBe((TradeSource.Broker, view.Trades.Id, s.Account.Id, "t:20260902:T1"));
        (trade.TradingSymbol, trade.Isin, trade.Exchange, trade.Side, trade.Qty, trade.Price, trade.Charges).ShouldBe(("EXMPL", null, "NSE", TradeSide.Buy, 10L, 90.5m, 0m));
        (trade.TradeDate, trade.TradeTime, trade.InstrumentId, trade.BatchId, trade.CreatedBy).ShouldBe((new DateOnly(2026, 9, 2), new TimeOnly(11, 20, 6), instrument.Id, null, Actor));
    }

    [Fact]
    public async Task The_identity_is_the_one_the_csv_reader_gives_a_trade_id_so_the_same_trade_is_never_stored_twice()
    {
        var s = await BrokerScenario.StartAsync();
        await s.Tradebook.ImportOkAsync(TradeScenario.Csv("2026-09-02,EXMPL,INE000A01011,buy,10,90,T1"), rebuild: false);
        s.Dhan.Trades.Add(Trade("T1", new DateOnly(2026, 9, 2), TradeSide.Buy, 10, 90m));

        var view = await s.SyncOkAsync();

        (view.Trades.Counts.Stored, view.Trades.Counts.Duplicates).ShouldBe((0, 1));
        s.Fx.Trades.Trades.Select(t => t.ExternalId).ShouldBe(["t:20260902:T1"]);
        TradeIdentity.ForTradeId(new DateOnly(2026, 9, 2), "T1").ShouldBe("t:20260902:T1");
    }

    [Fact]
    public async Task The_same_trade_id_on_another_day_is_another_trade()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Trades.Add(Trade("T1", new DateOnly(2026, 9, 2), TradeSide.Buy, 10, 90m));
        s.Dhan.Trades.Add(Trade("T1", new DateOnly(2026, 9, 3), TradeSide.Buy, 10, 91m));

        await s.SyncOkAsync();

        s.BrokerTrades().Select(t => t.ExternalId).Order().ShouldBe(["t:20260902:T1", "t:20260903:T1"]);
    }

    [Fact]
    public async Task A_trade_id_that_repeats_in_one_answer_is_stored_once()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Trades.Add(Trade("T1", new DateOnly(2026, 9, 2), TradeSide.Buy, 10, 90m));
        s.Dhan.Trades.Add(Trade("T1", new DateOnly(2026, 9, 2), TradeSide.Buy, 10, 90m));

        var view = await s.SyncOkAsync();

        (view.Trades.Counts.Stored, view.Trades.Counts.Duplicates).ShouldBe((1, 1));
    }

    [Fact]
    public async Task A_voided_broker_trade_is_stored_again_when_its_day_is_read_again()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Trades.AddRange(BaseTrades());
        await s.SyncOkAsync();
        s.BrokerTrades().First(t => t.ExternalId == "t:20260902:T1").Void(Actor, s.Fx.Clock.GetUtcNow()).ShouldBeTrue();

        var again = await s.SyncOkAsync();

        again.Trades.Counts.Stored.ShouldBe(1);
        s.BrokerTrades().Count(t => t.ExternalId == "t:20260902:T1").ShouldBe(2);
        s.BrokerTrades().Count(t => !t.IsVoided).ShouldBe(3);
    }

    [Fact]
    public async Task Unusable_trades_are_dropped_and_counted_and_skipped_ones_are_counted_by_reason()
    {
        var s = await BrokerScenario.StartAsync();
        var day = new DateOnly(2026, 10, 2);
        s.Dhan.Trades.Add(Trade("T1", day, TradeSide.Buy, 10, 90m));
        s.Dhan.Trades.Add(Trade("T2", day, TradeSide.Buy, 0, 90m)); // no quantity
        s.Dhan.Trades.Add(Trade("T3", day, TradeSide.Buy, 5, 0m)); // no price
        s.Dhan.Trades.Add(Trade(new string('x', 70), day, TradeSide.Buy, 5, 10m)); // an id that cannot be an identity
        s.Dhan.Trades.Add(Trade("T5", day, TradeSide.Buy, 5, 10m, symbol: " ")); // no ticker
        s.Dhan.SkippedPerWindow["derivatives"] = 2;
        s.Dhan.SkippedPerWindow["bse"] = 1;
        s.Dhan.DroppedPerWindow = 1;

        var counts = (await s.SyncOkAsync(from: new DateOnly(2026, 10, 1))).Trades.Counts;

        (counts.Stored, counts.Dropped, counts.Skipped).ShouldBe((1, 5, 3));
        counts.SkippedByReason.ShouldBe(new Dictionary<string, int> { ["bse"] = 1, ["derivatives"] = 2 });
        s.BrokerTrades().Count.ShouldBe(1);
    }

    [Fact]
    public async Task Trades_dated_after_today_or_before_1990_are_dropped()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.IgnoreDates = true;
        s.Dhan.Trades.Add(Trade("T1", new DateOnly(2026, 10, 10), TradeSide.Buy, 1, 10m));
        s.Dhan.Trades.Add(Trade("T2", new DateOnly(1989, 12, 29), TradeSide.Buy, 1, 10m));
        s.Dhan.Trades.Add(Trade("T3", new DateOnly(2026, 10, 11), TradeSide.Buy, 1, 10m));

        var counts = (await s.SyncOkAsync(from: new DateOnly(2026, 10, 10))).Trades.Counts;

        (counts.Stored, counts.Dropped).ShouldBe((1, 2));
    }

    [Fact]
    public async Task A_window_that_dhan_refuses_as_too_wide_is_halved_once_and_the_run_says_so()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Trades.AddRange(BaseTrades());
        s.Dhan.TradeFailure = (from, to, _) => to.DayNumber - from.DayNumber + 1 > 20 ? new BrokerFailure(BrokerFailureKind.Rejected, "DH-905") : null;

        var view = await s.SyncOkAsync();

        (view.Trades.Counts.Halved, view.Trades.Counts.Stored, view.Trades.Status).ShouldBe((1, 3, "succeeded"));
    }

    // ---- failures ----

    [Fact]
    public async Task A_window_that_fails_leaves_the_watermark_where_the_last_whole_window_put_it_and_the_next_run_reads_it_again()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Trades.AddRange(BaseTrades());
        s.Dhan.Trades.Add(Trade("T9", new DateOnly(2026, 10, 5), TradeSide.Buy, 1, 140m));
        s.Dhan.TradeFailure = (from, _, _) => from == new DateOnly(2026, 10, 1) ? new BrokerFailure(BrokerFailureKind.Unavailable) : null;

        var failed = await s.SyncOkAsync();

        failed.Ledger.ShouldBeNull();
        (failed.Trades.Status, failed.Trades.Counts.WindowsDone, failed.Trades.Counts.WindowsTotal, failed.Trades.Counts.Stored).ShouldBe(("failed", 1, 2, 3));
        failed.Trades.Error!.ShouldContain("could not be reached");
        s.Data.Watermarks[(s.Account.Id, BrokerSyncKind.Trades)].ShouldBe(new DateOnly(2026, 9, 30));
        s.BrokerTrades().Count.ShouldBe(3);

        s.Dhan.TradeFailure = null;
        var next = await s.SyncOkAsync(defaultFrom: true);

        (next.Trades.From, next.Trades.Status, next.Trades.Counts.Stored).ShouldBe((new DateOnly(2026, 9, 30), "succeeded", 1));
        s.Data.Watermarks[(s.Account.Id, BrokerSyncKind.Trades)].ShouldBe(new DateOnly(2026, 10, 10));
    }

    [Fact]
    public async Task A_refused_token_fails_the_run_as_not_connected_and_the_error_is_a_safe_text()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.TradeFailure = (_, _, _) => new BrokerFailure(BrokerFailureKind.Unauthorized);

        var view = await s.SyncOkAsync();

        (view.Trades.Status, view.Trades.Error).ShouldBe(("failed", PortfolioErrors.TokenRefused.Message));
        s.Data.Watermarks.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_window_the_database_refuses_as_a_whole_is_saved_row_by_row_and_the_watermark_moves()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Trades.AddRange(BaseTrades());
        s.Fx.Data.CollidingSaves = 1; // a tradebook import stored some of the same trades meanwhile: the one save of the whole window is refused

        var view = await s.SyncOkAsync();

        (view.Trades.Status, view.Trades.Counts.WindowsDone, view.Trades.Counts.Stored, view.Trades.Counts.Dropped).ShouldBe(("succeeded", 2, 3, 0));
        s.BrokerTrades().Count.ShouldBe(3);
        s.Data.Watermarks[(s.Account.Id, BrokerSyncKind.Trades)].ShouldBe(new DateOnly(2026, 10, 10));
    }

    [Fact]
    public async Task A_row_the_database_keeps_refusing_is_dropped_and_counted_and_never_blocks_the_windows_after_it()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Trades.AddRange(BaseTrades());
        s.Dhan.Trades.Add(Trade("T9", new DateOnly(2026, 10, 5), TradeSide.Buy, 1, 140m));
        s.Fx.Data.CollidingSaves = 2; // the whole window is refused, and so is its first row (T1)

        var view = await s.SyncOkAsync();

        (view.Trades.Status, view.Trades.Counts.WindowsDone, view.Trades.Counts.Stored, view.Trades.Counts.Dropped).ShouldBe(("succeeded", 2, 3, 1));
        s.BrokerTrades().Select(t => t.ExternalId).Order().ShouldBe(["t:20260915:T2", "t:20260920:T3", "t:20261005:T9"]);
        s.Data.Watermarks[(s.Account.Id, BrokerSyncKind.Trades)].ShouldBe(new DateOnly(2026, 10, 10));
    }

    [Fact]
    public async Task A_crash_while_storing_a_window_ends_the_run_as_failed_with_a_fixed_text()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Trades.AddRange(BaseTrades());
        s.Fx.Data.FailTransactionWith = new InvalidOperationException("secret detail that must not be shown: INE000A01011");

        var view = await s.SyncOkAsync();

        (view.Trades.Status, view.Trades.Error).ShouldBe(("failed", PortfolioErrors.SyncFailed.Message));
    }

    // ---- who may run ----

    [Fact]
    public async Task A_run_is_refused_while_a_holdings_sync_of_the_account_runs()
    {
        var s = await BrokerScenario.StartAsync();
        s.Fx.Data.Syncs.Add(HoldingSync.Start(s.Account.Id, SyncTrigger.Manual, Actor, s.Fx.Clock.GetUtcNow()));

        var result = await s.SyncAsync();

        result.Error.ShouldBe(PortfolioErrors.SyncRunning);
        s.Dhan.TradeCalls.ShouldBeEmpty();
        s.Data.Runs.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_run_is_refused_while_another_broker_history_run_of_the_account_runs()
    {
        var s = await BrokerScenario.StartAsync();
        s.Data.Runs.Add(BrokerSyncRun.Start(s.Account.Id, BrokerSyncKind.Ledger, BrokerSyncTrigger.Manual, From, From, Actor, s.Fx.Clock.GetUtcNow()));

        var result = await s.SyncAsync();

        result.Error.Code.ShouldBe("portfolio.sync_running");
    }

    [Fact]
    public async Task A_run_is_not_started_for_an_account_without_a_connection_or_with_an_expired_token_or_without_the_portfolio_role()
    {
        var s = await BrokerScenario.StartAsync();
        var unconnected = await s.Fx.ConnectedAsync("Example Two", "1000000002", connect: false);
        var expiredAt = s.Fx.Clock.GetUtcNow();

        var notConnected = await s.Sync().HandleAsync(new XDrishti.Application.Portfolio.BrokerHistory.SyncBrokerHistoryCommand(unconnected.Id, false, null, Actor), CancellationToken.None);
        s.Fx.Clock.Advance(TimeSpan.FromDays(3));
        var expired = await s.SyncAsync();
        var missing = await s.Sync().HandleAsync(new XDrishti.Application.Portfolio.BrokerHistory.SyncBrokerHistoryCommand(Guid.NewGuid(), false, null, Actor), CancellationToken.None);

        notConnected.Error.Code.ShouldBe("portfolio.not_connected");
        expired.Error.Code.ShouldBe("portfolio.token_expired");
        missing.Error.Code.ShouldBe("accounts.not_found");
        s.Dhan.TradeCalls.ShouldBeEmpty();
        expiredAt.ShouldBeLessThan(s.Fx.Clock.GetUtcNow());
    }

    [Fact]
    public async Task A_scheduled_run_that_finds_the_token_expired_is_recorded_as_skipped()
    {
        var s = await BrokerScenario.StartAsync();
        s.Fx.Clock.Advance(TimeSpan.FromDays(3));

        var result = await s.SyncAsync(scheduled: true);

        result.Error.Code.ShouldBe("portfolio.token_expired");
        var run = s.Data.Runs.ShouldHaveSingleItem();
        (run.Status, run.Kind, run.Trigger, run.Error).ShouldBe((SyncStatus.Skipped, BrokerSyncKind.Trades, BrokerSyncTrigger.Scheduled, "portfolio.token_expired"));
    }

    // ---- the run row ----

    [Fact]
    public async Task The_trigger_is_manual_for_the_owner_first_for_the_first_scheduled_run_and_scheduled_after_that()
    {
        var s = await BrokerScenario.StartAsync();

        await s.SyncOkAsync();
        s.Fx.Clock.Advance(TimeSpan.FromMinutes(1));
        await s.SyncOkAsync(scheduled: true, defaultFrom: true);

        s.RunsOf(BrokerSyncKind.Trades).Select(r => r.Trigger).ShouldBe([BrokerSyncTrigger.Manual, BrokerSyncTrigger.Scheduled]);
        s.Data.Runs.Clear();
        s.Data.Watermarks.Clear();
        await s.SyncOkAsync(scheduled: true, defaultFrom: true);
        s.RunsOf(BrokerSyncKind.Trades).Single().Trigger.ShouldBe(BrokerSyncTrigger.First);
    }

    [Fact]
    public async Task The_audit_event_has_counts_only_and_a_scheduled_run_with_nothing_new_leaves_none()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Trades.AddRange(BaseTrades());

        await s.SyncOkAsync();
        await s.SyncOkAsync(scheduled: true);

        var events = s.Fx.Events(AccountEventType.BrokerHistorySynced);
        events.Count.ShouldBe(2); // the trades and the ledger of the manual run; the scheduled one found nothing new
        var trades = events.Single(e => e.Detail!.StartsWith("Broker trades", StringComparison.Ordinal));
        trades.Detail.ShouldBe("Broker trades read (manual): 2 windows, 3 new trades, 0 already stored, 0 skipped, 0 unusable; 1 holdings rebuilt, 0 need review");
        events.ShouldAllBe(e => e.AccountId == s.Account.Id && e.Actor == Actor && !e.Detail!.Contains("EXMPL") && !e.Detail.Contains("130") && !e.Detail.Contains("T1"));
    }

    [Fact]
    public async Task The_token_goes_to_the_broker_and_nowhere_else()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Trades.AddRange(BaseTrades());

        var view = await s.SyncOkAsync();

        s.Dhan.Tokens.ShouldNotBeEmpty();
        var token = s.Dhan.Tokens[0].Reveal();
        s.Dhan.Tokens.ShouldAllBe(t => t.Reveal() == token);
        System.Text.Json.JsonSerializer.Serialize(view).ShouldNotContain(token);
        s.Fx.Accounts.Events.ShouldAllBe(e => !(e.Detail ?? string.Empty).Contains(token));
    }
}
