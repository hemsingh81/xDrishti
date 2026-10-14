using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Portfolio.BrokerHistory;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.BrokerHistory.BrokerScenario;
using static XDrishti.Application.Tests.Portfolio.PortfolioFixture;

namespace XDrishti.Application.Tests.Portfolio.BrokerHistory;

/// <summary>
/// What is news after a run (ADR 0025): a scheduled run leaves an audit event only when it stored something, rebuilt a holding or changed which holdings need review - the same hand-dated holding is not news every evening - and the status keeps the
/// figures of the last successful run when a later run was skipped (token expired) or failed.
/// </summary>
public sealed class BrokerRunNewsTests
{
    private static async Task<BrokerScenario> HandDatedAsync()
    {
        var s = await BrokerScenario.StartAsync();
        s.Holding.ChangeLotDate(s.Holding.Lots.Single().Id, new DateOnly(2023, 2, 2), s.Fx.Today).IsSuccess.ShouldBeTrue();
        s.Dhan.Trades.AddRange(BaseTrades());
        return s;
    }

    private static async Task<BrokerHistoryStatusView> StatusAsync(BrokerScenario s) =>
        Ok(await s.Status().HandleAsync(new GetBrokerHistoryStatusQuery(s.Account.Id), CancellationToken.None));

    [Fact]
    public async Task A_scheduled_run_that_finds_the_same_holdings_needing_review_leaves_no_audit_event()
    {
        var s = await HandDatedAsync();
        await s.SyncOkAsync(); // the owner's run: audited (trades and ledger)
        var events = s.Fx.Events(AccountEventType.BrokerHistorySynced).Count;

        var first = await s.SyncOkAsync(scheduled: true);
        s.Fx.Clock.Advance(TimeSpan.FromMinutes(1));
        var second = await s.SyncOkAsync(scheduled: true, defaultFrom: true);

        (first.Trades.Counts.NeedsReview, second.Trades.Counts.NeedsReview).ShouldBe((1, 1));
        s.Fx.Events(AccountEventType.BrokerHistorySynced).Count.ShouldBe(events);
    }

    [Fact]
    public async Task A_scheduled_run_that_changes_which_holdings_need_review_is_audited()
    {
        var s = await HandDatedAsync();
        await s.SyncOkAsync();
        var events = s.Fx.Events(AccountEventType.BrokerHistorySynced).Count;
        Ok(await s.Tradebook.RebuildAsync(dryRun: false)); // the owner rebuilds that holding with the preview-then-rebuild: it no longer needs review
        s.Fx.Clock.Advance(TimeSpan.FromMinutes(1));

        var next = await s.SyncOkAsync(scheduled: true, defaultFrom: true);

        (next.Trades.Counts.NeedsReview, next.Trades.Counts.Unchanged).ShouldBe((0, 1));
        s.Fx.Events(AccountEventType.BrokerHistorySynced).Count.ShouldBe(events + 1);
    }

    [Fact]
    public async Task A_scheduled_run_that_needs_review_for_the_first_time_is_audited()
    {
        var s = await HandDatedAsync();

        await s.SyncOkAsync(scheduled: true);

        s.Fx.Events(AccountEventType.BrokerHistorySynced).Select(e => e.Detail!).ShouldContain(d => d.StartsWith("Broker trades", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_skipped_run_does_not_erase_the_rebuilt_and_review_figures_of_the_status()
    {
        var s = await HandDatedAsync();
        await s.SyncOkAsync();
        s.Fx.Clock.Advance(TimeSpan.FromDays(3)); // the token has expired
        (await s.SyncAsync(scheduled: true)).Error.Code.ShouldBe("portfolio.token_expired");

        var status = await StatusAsync(s);

        status.Trades.LastRun!.Status.ShouldBe("skipped");
        (status.HoldingsRebuilt, status.HoldingsNeedingReview).ShouldBe((0, 1));
        status.NeedsReviewSymbols.ShouldBe(["EXMPL"]); // the symbols come from the run that succeeded, not from the skipped one that shows no counts
    }

    [Fact]
    public async Task A_failed_run_does_not_erase_them_either()
    {
        var s = await HandDatedAsync();
        await s.SyncOkAsync();
        s.Dhan.TradeFailure = (_, _, _) => new BrokerFailure(BrokerFailureKind.Unavailable);
        s.Fx.Clock.Advance(TimeSpan.FromMinutes(1));
        (await s.SyncOkAsync(defaultFrom: true)).Trades.Status.ShouldBe("failed");

        var status = await StatusAsync(s);

        status.Trades.LastRun!.Status.ShouldBe("failed");
        status.HoldingsNeedingReview.ShouldBe(1);
        status.NeedsReviewSymbols.ShouldBe(["EXMPL"]);
        status.Trades.LastRun.Counts.NotRebuilt.ShouldBeEmpty(); // what the UI used to read: the failed run's own counts have none
    }

    [Fact]
    public async Task A_run_that_is_still_running_does_not_erase_the_review_symbols_either()
    {
        var s = await HandDatedAsync();
        await s.SyncOkAsync();
        IReadOnlyList<string>? whileRunning = null;
        var runningSeen = false;
        s.Fx.Clock.Advance(TimeSpan.FromMinutes(1));
        s.Dhan.BeforeAnswer = async () =>
        {
            var seen = await StatusAsync(s);
            runningSeen |= seen.Running;
            whileRunning = seen.NeedsReviewSymbols;
        };

        await s.SyncOkAsync(defaultFrom: true);

        runningSeen.ShouldBeTrue();
        whileRunning.ShouldBe(["EXMPL"]);
    }

    [Fact]
    public async Task The_symbols_are_only_those_that_need_review_and_none_when_no_run_succeeded_yet()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Trades.Add(Trade("T1", new DateOnly(2026, 9, 2), TradeSide.Buy, 5, 50m, symbol: "GONE")); // stored, no holding: listed as no_holding, not as needing review
        (await StatusAsync(s)).NeedsReviewSymbols.ShouldBeEmpty();

        await s.SyncOkAsync();

        var status = await StatusAsync(s);
        status.Trades.LastRun!.Counts.NotRebuilt.ShouldBe([new BrokerNotRebuilt("GONE", "no_holding")]);
        status.NeedsReviewSymbols.ShouldBeEmpty();
    }
}
