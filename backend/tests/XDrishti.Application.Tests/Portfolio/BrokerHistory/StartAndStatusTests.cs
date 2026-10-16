using XDrishti.Application.Portfolio;
using XDrishti.Application.Portfolio.BrokerHistory;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.BrokerHistory.BrokerScenario;
using static XDrishti.Application.Tests.Portfolio.PortfolioFixture;

namespace XDrishti.Application.Tests.Portfolio.BrokerHistory;

/// <summary>"Sync now" over the API path (ADR 0025, P2-13 criterion 10): the run is registered and handed to the background worker, the worker carries it on, the status tells how far it is, and an interrupted run is ended at start.</summary>
public sealed class StartAndStatusTests
{
    private static Task<Domain.Common.Result<BrokerHistoryRunView>> StartAsync(BrokerScenario s, DateOnly? from = null) =>
        s.Start().HandleAsync(new StartBrokerHistoryCommand(s.Account.Id, from ?? From, Actor), CancellationToken.None).AsTask();

    [Fact]
    public async Task Sync_now_registers_the_trades_run_hands_it_on_and_answers_with_it_running()
    {
        var s = await BrokerScenario.StartAsync();

        var view = Ok(await StartAsync(s));

        (view.Trades.Kind, view.Trades.Status, view.Trades.Trigger, view.Trades.From, view.Trades.To).ShouldBe(("trades", "running", "manual", From, new DateOnly(2026, 10, 10)));
        view.Ledger.ShouldBeNull();
        s.Queue.Tickets.ShouldBe([new XDrishti.Application.Abstractions.Portfolio.BrokerHistoryTicket(view.Trades.Id, s.Account.Id, From, Actor)]);
        s.Dhan.TradeCalls.ShouldBeEmpty(); // nothing is read until the worker takes it
    }

    [Fact]
    public async Task A_second_sync_now_while_the_first_runs_is_refused_with_sync_running()
    {
        var s = await BrokerScenario.StartAsync();
        await StartAsync(s);

        var second = await StartAsync(s);

        second.Error.Code.ShouldBe("portfolio.sync_running");
        s.Queue.Tickets.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Sync_now_is_refused_while_a_holdings_sync_runs()
    {
        var s = await BrokerScenario.StartAsync();
        s.Fx.Data.Syncs.Add(HoldingSync.Start(s.Account.Id, SyncTrigger.Manual, Actor, s.Fx.Clock.GetUtcNow()));

        var result = await StartAsync(s);

        result.Error.Code.ShouldBe("portfolio.sync_running");
        s.Queue.Tickets.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_worker_carries_the_registered_run_on_to_the_ledger()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Trades.AddRange(BaseTrades());
        s.Dhan.Ledger.Add(Line(new DateOnly(2026, 9, 2), "V1", "RECEIPT", "FUNDS DEPOSIT", 100m));
        var started = Ok(await StartAsync(s));

        var done = await s.Execute().HandleAsync(new ExecuteBrokerHistoryCommand(s.Queue.Tickets.Single()), CancellationToken.None);

        done.IsSuccess.ShouldBeTrue();
        var trades = s.RunsOf(BrokerSyncKind.Trades).Single();
        (trades.Id, trades.Status, trades.Counts.Stored).ShouldBe((started.Trades.Id, SyncStatus.Succeeded, 3));
        s.RunsOf(BrokerSyncKind.Ledger).Single().Status.ShouldBe(SyncStatus.Succeeded);
        s.Data.Entries.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_token_that_expired_while_the_run_waited_ends_the_run_as_failed()
    {
        var s = await BrokerScenario.StartAsync();
        Ok(await StartAsync(s));
        s.Fx.Clock.Advance(TimeSpan.FromDays(3));

        await s.Execute().HandleAsync(new ExecuteBrokerHistoryCommand(s.Queue.Tickets.Single()), CancellationToken.None);

        var run = s.RunsOf(BrokerSyncKind.Trades).Single();
        (run.Status, run.Error).ShouldBe((SyncStatus.Failed, PortfolioErrors.TokenExpired.Message));
        s.Dhan.TradeCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_run_that_already_ended_is_not_carried_out_again()
    {
        var s = await BrokerScenario.StartAsync();
        var started = Ok(await StartAsync(s));
        await s.Execute().HandleAsync(new ExecuteBrokerHistoryCommand(s.Queue.Tickets.Single()), CancellationToken.None);
        s.Dhan.TradeCalls.Clear();

        await s.Execute().HandleAsync(new ExecuteBrokerHistoryCommand(new XDrishti.Application.Abstractions.Portfolio.BrokerHistoryTicket(started.Trades.Id, s.Account.Id, From, Actor)), CancellationToken.None);

        s.Dhan.TradeCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_run_still_marked_running_by_a_process_that_ended_is_failed_at_start()
    {
        var s = await BrokerScenario.StartAsync();
        Ok(await StartAsync(s));
        s.Fx.Clock.Advance(TimeSpan.FromMinutes(5));

        await s.Fx.Recover().HandleAsync(new RecoverInterruptedSyncsCommand(s.Fx.Clock.GetUtcNow()), CancellationToken.None);

        var run = s.RunsOf(BrokerSyncKind.Trades).Single();
        (run.Status, run.Error).ShouldBe((SyncStatus.Failed, "The sync did not finish (the application stopped or crashed while it ran)."));
        (await s.SyncAsync()).IsSuccess.ShouldBeTrue();
    }

    // ---- the status ----

    [Fact]
    public async Task The_status_of_an_account_never_read_is_empty_and_has_the_next_scheduled_run()
    {
        var s = await BrokerScenario.StartAsync();

        var status = Ok(await s.Status().HandleAsync(new GetBrokerHistoryStatusQuery(s.Account.Id), CancellationToken.None));

        (status.Running, status.Trades.Watermark, status.Trades.LastRun, status.Ledger.LastRun, status.HoldingsRebuilt, status.HoldingsNeedingReview).ShouldBe((false, null, null, null, 0, 0));
        (status.Trades.WindowsDone, status.Trades.WindowsTotal).ShouldBe((0, 0));
        status.NextRunAt.ShouldBe(FixedSchedules.Next);
    }

    [Fact]
    public async Task The_status_while_a_run_runs_says_how_many_windows_are_done()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Trades.AddRange(BaseTrades());
        s.Dhan.TradeFailure = (from, _, _) => from == new DateOnly(2026, 10, 1) ? new XDrishti.Application.Abstractions.Accounts.BrokerFailure(XDrishti.Application.Abstractions.Accounts.BrokerFailureKind.Unavailable) : null;
        var observed = new List<(bool Running, int Done, int Total)>();
        s.Dhan.BeforeAnswer = async () =>
        {
            var seen = Ok(await s.Status().HandleAsync(new GetBrokerHistoryStatusQuery(s.Account.Id), CancellationToken.None));
            observed.Add((seen.Running, seen.Trades.WindowsDone, seen.Trades.WindowsTotal));
        };

        await s.SyncOkAsync();

        observed.Distinct().ShouldContain((true, 0, 2));
        observed.Distinct().ShouldContain((true, 1, 2));
    }

    [Fact]
    public async Task The_status_after_a_run_has_the_watermarks_the_last_runs_and_the_rebuild_figures()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Trades.AddRange(BaseTrades());
        await s.SyncOkAsync();

        var status = Ok(await s.Status().HandleAsync(new GetBrokerHistoryStatusQuery(s.Account.Id), CancellationToken.None));

        (status.Running, status.Trades.Watermark, status.Ledger.Watermark).ShouldBe((false, new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 10)));
        (status.Trades.LastRun!.Status, status.Trades.WindowsDone, status.Trades.WindowsTotal, status.Ledger.LastRun!.Status).ShouldBe(("succeeded", 2, 2, "succeeded"));
        (status.HoldingsRebuilt, status.HoldingsNeedingReview).ShouldBe((1, 0));
    }

    [Fact]
    public async Task The_status_counts_the_holdings_that_need_review()
    {
        var s = await BrokerScenario.StartAsync();
        s.Holding.ChangeLotDate(s.Holding.Lots.Single().Id, new DateOnly(2023, 2, 2), s.Fx.Today).IsSuccess.ShouldBeTrue();
        s.Dhan.Trades.AddRange(BaseTrades());
        await s.SyncOkAsync();

        var status = Ok(await s.Status().HandleAsync(new GetBrokerHistoryStatusQuery(s.Account.Id), CancellationToken.None));

        (status.HoldingsRebuilt, status.HoldingsNeedingReview).ShouldBe((0, 1));
    }

    [Fact]
    public async Task The_status_of_an_unknown_account_is_not_found()
    {
        var s = await BrokerScenario.StartAsync();

        (await s.Status().HandleAsync(new GetBrokerHistoryStatusQuery(Guid.NewGuid()), CancellationToken.None)).Error.Code.ShouldBe("accounts.not_found");
    }
}
