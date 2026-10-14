using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Portfolio.BrokerHistory;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.BrokerHistory.BrokerScenario;
using static XDrishti.Application.Tests.Portfolio.PortfolioFixture;

namespace XDrishti.Application.Tests.Portfolio.BrokerHistory;

/// <summary>
/// The watermark means "every date up to here was read" (ADR 0025): a start date later than the day after it would leave a hole that is never read, so "Sync now" refuses it (<c>400 portfolio.invalid</c>, field <c>from</c>) for both kinds; an
/// earlier start date only reads dates again, which stores nothing twice.
/// </summary>
public sealed class BrokerWatermarkGapTests
{
    private static readonly DateOnly Oct1 = new(2026, 10, 1);

    private static readonly DateOnly Sep30 = new(2026, 9, 30);

    private const string Advice = "Start on or before 2026-10-01: dates in between were never read.";

    private const string FirstImportAdvice = "Start on or before 2025-01-01: dates in between were never read.";

    /// <summary>An account whose trades were read up to 30 September (the October window failed).</summary>
    private static async Task<BrokerScenario> TradesReadUpToSeptemberAsync()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Trades.AddRange(BaseTrades());
        s.Dhan.TradeFailure = (from, _, _) => from == Oct1 ? new BrokerFailure(BrokerFailureKind.Unavailable) : null;
        (await s.SyncOkAsync()).Trades.Status.ShouldBe("failed");
        s.Dhan.TradeFailure = null;
        s.Dhan.TradeCalls.Clear();
        return s;
    }

    [Fact]
    public async Task A_start_later_than_the_day_after_the_trades_watermark_is_refused_and_nothing_changes()
    {
        var s = await TradesReadUpToSeptemberAsync();
        var runs = s.Data.Runs.Count;

        var refused = await s.SyncAsync(from: new DateOnly(2026, 10, 5));

        refused.Error.Code.ShouldBe("portfolio.invalid");
        FieldsOf(refused).ShouldBe(["from"]);
        refused.Error.Fields!.Single().Message.ShouldBe(FirstImportAdvice); // the ledger was never read, so it may not start later than its first import date either
        s.Data.Watermarks[(s.Account.Id, BrokerSyncKind.Trades)].ShouldBe(Sep30);
        s.Data.Runs.Count.ShouldBe(runs);
        s.Dhan.TradeCalls.ShouldBeEmpty();
        s.Dhan.LedgerCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Sync_now_hands_nothing_on_for_such_a_start_date_either()
    {
        var s = await TradesReadUpToSeptemberAsync();
        var runs = s.Data.Runs.Count;

        var refused = await s.Start().HandleAsync(new StartBrokerHistoryCommand(s.Account.Id, new DateOnly(2026, 10, 5), Actor), CancellationToken.None);

        FieldsOf(refused).ShouldBe(["from"]);
        s.Queue.Tickets.ShouldBeEmpty();
        s.Data.Runs.Count.ShouldBe(runs);
    }

    [Fact]
    public async Task A_start_later_than_the_day_after_the_ledger_watermark_is_refused_too()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.LedgerFailure = (from, _) => from == Oct1 ? new BrokerFailure(BrokerFailureKind.Unavailable) : null;
        var failed = await s.SyncOkAsync();
        (failed.Trades.Status, failed.Ledger!.Status).ShouldBe(("succeeded", "failed"));
        s.Dhan.LedgerFailure = null;
        s.Dhan.TradeCalls.Clear();

        var refused = await s.SyncAsync(from: new DateOnly(2026, 10, 5)); // the trades reach 10 October, the ledger only 30 September

        FieldsOf(refused).ShouldBe(["from"]);
        refused.Error.Fields!.Single().Message.ShouldBe(Advice);
        s.Data.Watermarks[(s.Account.Id, BrokerSyncKind.Ledger)].ShouldBe(Sep30);
        s.Dhan.TradeCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_day_after_the_watermark_is_allowed_when_both_kinds_were_read_and_continues_without_a_hole()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Trades.AddRange(BaseTrades());
        s.Dhan.LedgerFailure = (from, _) => from == Oct1 ? new BrokerFailure(BrokerFailureKind.Unavailable) : null;
        (await s.SyncOkAsync()).Ledger!.Status.ShouldBe("failed"); // the trades reach 10 October, the ledger 30 September: both were read
        s.Dhan.LedgerFailure = null;

        var view = await s.SyncOkAsync(from: Oct1);

        (view.Trades.Status, view.Trades.From, view.Ledger!.Status, view.Ledger.From).ShouldBe(("succeeded", Oct1, "succeeded", Oct1));
        s.Data.Watermarks[(s.Account.Id, BrokerSyncKind.Ledger)].ShouldBe(new DateOnly(2026, 10, 10));
    }

    [Fact]
    public async Task A_kind_that_was_never_read_while_the_other_was_does_not_allow_a_start_after_the_first_import_date()
    {
        var s = await TradesReadUpToSeptemberAsync(); // the trades reach 30 September, the ledger was never read
        var runs = s.Data.Runs.Count;

        var refused = await s.SyncAsync(from: Oct1); // the day after the trades watermark, but the ledger would then begin on 1 October and lose its history

        FieldsOf(refused).ShouldBe(["from"]);
        refused.Error.Fields!.Single().Message.ShouldBe(FirstImportAdvice);
        s.Data.Runs.Count.ShouldBe(runs);
        s.Dhan.TradeCalls.ShouldBeEmpty();
        s.Data.Watermarks.ContainsKey((s.Account.Id, BrokerSyncKind.Ledger)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_start_on_or_before_the_first_import_date_is_allowed_while_a_kind_was_never_read_and_the_default_start_reads_it_from_there()
    {
        var s = await TradesReadUpToSeptemberAsync();

        var firstImportDate = await s.SyncOkAsync(from: new DateOnly(2025, 1, 1));
        var ledgerWas = firstImportDate.Ledger!.From;

        (firstImportDate.Trades.Status, ledgerWas).ShouldBe(("succeeded", new DateOnly(2025, 1, 1)));
    }

    [Fact]
    public async Task The_default_start_of_an_unread_ledger_is_the_first_import_date_whatever_the_trades_watermark_says()
    {
        var s = await TradesReadUpToSeptemberAsync();

        var view = await s.SyncOkAsync(defaultFrom: true);

        (view.Trades.From, view.Ledger!.From).ShouldBe((Sep30, new DateOnly(2025, 1, 1))); // the trades re-read the last date they have, the ledger starts at the beginning
    }

    [Fact]
    public async Task An_earlier_start_date_reads_the_dates_again_and_stores_nothing_twice()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Trades.AddRange(BaseTrades());
        await s.SyncOkAsync();

        var again = await s.SyncOkAsync(from: new DateOnly(2026, 9, 10));

        (again.Trades.From, again.Trades.Counts.Stored, again.Trades.Counts.Duplicates).ShouldBe((new DateOnly(2026, 9, 10), 0, 2)); // T2 and T3
        s.Data.Watermarks[(s.Account.Id, BrokerSyncKind.Trades)].ShouldBe(new DateOnly(2026, 10, 10));
    }

    [Fact]
    public async Task An_earlier_start_date_also_reads_older_ledger_entries_and_moves_the_date_the_ledger_is_known_from_back()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Ledger.AddRange(
        [
            BrokerScenario.Line(new DateOnly(2026, 9, 5), "V9", "JOURNAL", "DP CHARGES 05-09-2026 EXMPL", -15.93m),
            BrokerScenario.Line(new DateOnly(2026, 6, 2), "V1", "JOURNAL", "AMC CHARGES 02-06-2026", -295m),
        ]);
        await s.SyncOkAsync();
        var coverageBefore = Ok(await s.Ledger().HandleAsync(new ListLedgerQuery(s.Account.Id, null, null, null, null, null), CancellationToken.None)).CoverageFrom;

        var again = await s.SyncOkAsync(from: new DateOnly(2026, 5, 1));

        var coverageAfter = Ok(await s.Ledger().HandleAsync(new ListLedgerQuery(s.Account.Id, null, null, null, null, null), CancellationToken.None)).CoverageFrom;
        (again.Ledger!.From, again.Ledger.Counts.Stored, again.Ledger.Counts.Duplicates).ShouldBe((new DateOnly(2026, 5, 1), 1, 1));
        (coverageBefore, coverageAfter).ShouldBe((new DateOnly(2026, 9, 5), new DateOnly(2026, 6, 2)));
    }

    [Fact]
    public async Task An_account_that_was_never_read_may_start_at_any_date()
    {
        var s = await BrokerScenario.StartAsync();

        var view = await s.SyncOkAsync(from: new DateOnly(2026, 10, 5));

        (view.Trades.Status, view.Trades.From).ShouldBe(("succeeded", new DateOnly(2026, 10, 5)));
    }

    [Fact]
    public async Task A_scheduled_run_after_a_normal_run_continues_from_the_watermark_and_rebuilds_what_was_left()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Trades.AddRange(BaseTrades());
        s.Fx.Data.RacedRuns = 1; // the normal run's rebuild was skipped (the holding changed meanwhile)
        var manual = await s.SyncOkAsync();
        manual.Trades.Counts.Rebuilt.ShouldBe(0);
        s.Fx.Clock.Advance(TimeSpan.FromHours(10)); // 00:30 the next day, the token (valid for a day) is still good

        var scheduled = await s.SyncOkAsync(scheduled: true, defaultFrom: true);

        (scheduled.Trades.From, scheduled.Trades.Trigger, scheduled.Trades.Counts.WindowsTotal, scheduled.Trades.Counts.Rebuilt).ShouldBe((new DateOnly(2026, 10, 10), "scheduled", 1, 1));
        s.Data.Watermarks[(s.Account.Id, BrokerSyncKind.Trades)].ShouldBe(new DateOnly(2026, 10, 11));
    }
}
