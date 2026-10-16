using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Portfolio;
using XDrishti.Application.Portfolio.BrokerHistory;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.BrokerHistory.BrokerScenario;
using static XDrishti.Application.Tests.Portfolio.PortfolioFixture;

namespace XDrishti.Application.Tests.Portfolio.BrokerHistory;

/// <summary>Every way out of a run ends the run record (ADR 0025): a run left <c>running</c> would block the account (holdings syncs included) until the cleanup. Failures end it as failed with a plain text; the application stopping ends it as interrupted.</summary>
public sealed class BrokerRunEndingTests
{
    private const string Interrupted = "The sync was interrupted because the application is stopping.";

    private static async Task<(BrokerScenario Scenario, BrokerSyncRun Run)> RegisteredAsync()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Trades.AddRange(BaseTrades());
        Ok(await s.Start().HandleAsync(new StartBrokerHistoryCommand(s.Account.Id, From, Actor), CancellationToken.None));
        return (s, s.RunsOf(BrokerSyncKind.Trades).Single());
    }

    private static async Task AssertAccountIsFreeAsync(BrokerScenario s)
    {
        (await s.Data.IsRunningAsync(s.Account.Id, CancellationToken.None)).ShouldBeFalse();
        s.Data.FailWatermarkReadWith = null;
        s.Fx.Accounts.FailReadOnlyReadsWith = null;
        (await s.SyncAsync()).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_failure_outside_the_window_loop_ends_the_run_as_failed_with_a_plain_text_and_frees_the_account()
    {
        var (s, run) = await RegisteredAsync();
        s.Data.FailWatermarkReadWith = new InvalidOperationException("secret detail that must not be shown: INE000A01011"); // the very first thing the run does outside its try

        var done = await s.Execute().HandleAsync(new ExecuteBrokerHistoryCommand(s.Queue.Tickets.Single()), CancellationToken.None);

        done.IsSuccess.ShouldBeTrue();
        (run.Status, run.Error).ShouldBe((SyncStatus.Failed, PortfolioErrors.SyncFailed.Message));
        run.FinishedAt.ShouldNotBeNull();
        await AssertAccountIsFreeAsync(s);
    }

    [Fact]
    public async Task A_failure_of_the_use_case_before_it_reaches_the_pipeline_ends_the_run_as_failed()
    {
        var (s, run) = await RegisteredAsync();
        s.Fx.Accounts.FailReadOnlyReadsWith = new InvalidOperationException("the database went away");

        var done = await s.Execute().HandleAsync(new ExecuteBrokerHistoryCommand(s.Queue.Tickets.Single()), CancellationToken.None);

        done.Error.ShouldBe(PortfolioErrors.SyncFailed);
        (run.Status, run.Error).ShouldBe((SyncStatus.Failed, PortfolioErrors.SyncFailed.Message));
        await AssertAccountIsFreeAsync(s);
    }

    [Fact]
    public async Task Stopping_the_application_in_the_middle_of_a_window_ends_the_run_as_interrupted_and_passes_the_cancellation_on()
    {
        var (s, run) = await RegisteredAsync();
        using var stopping = new CancellationTokenSource();
        s.Dhan.BeforeAnswer = () =>
        {
            stopping.Cancel();
            return Task.CompletedTask;
        };

        await Should.ThrowAsync<OperationCanceledException>(async () => await s.Execute().HandleAsync(new ExecuteBrokerHistoryCommand(s.Queue.Tickets.Single()), stopping.Token));

        (run.Status, run.Error).ShouldBe((SyncStatus.Failed, Interrupted));
        s.BrokerTrades().ShouldBeEmpty();
        s.Data.Watermarks.ShouldBeEmpty();
        s.Dhan.BeforeAnswer = null;
        await AssertAccountIsFreeAsync(s);
    }

    [Fact]
    public async Task Stopping_the_application_outside_the_window_loop_ends_the_run_as_interrupted_too()
    {
        var (s, run) = await RegisteredAsync();
        using var stopping = new CancellationTokenSource();
        await stopping.CancelAsync();
        s.Data.FailWatermarkReadWith = new OperationCanceledException(stopping.Token);

        await Should.ThrowAsync<OperationCanceledException>(async () => await s.Execute().HandleAsync(new ExecuteBrokerHistoryCommand(s.Queue.Tickets.Single()), stopping.Token));

        (run.Status, run.Error).ShouldBe((SyncStatus.Failed, Interrupted));
        await AssertAccountIsFreeAsync(s);
    }

    [Fact]
    public async Task A_failure_while_the_ledger_run_is_ending_still_ends_the_ledger_run_as_failed_and_frees_the_account()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.LedgerFailure = (_, _) => new BrokerFailure(BrokerFailureKind.Unavailable);
        var failedToComplete = 0;
        s.Data.BeforeComplete = run =>
        {
            // The ledger run is failed by its own handling (twice: the failure, then the catch around it) and both writes fail - the database went away - so it is left to the outer handler of the pipeline, which must know the run.
            if (run.Kind == BrokerSyncKind.Ledger && failedToComplete < 2)
            {
                failedToComplete++;
                return new InvalidOperationException("the database went away");
            }

            return null;
        };

        var view = await s.SyncOkAsync();

        failedToComplete.ShouldBe(2);
        var ledger = s.RunsOf(BrokerSyncKind.Ledger).Single();
        (ledger.Status, ledger.Error).ShouldBe((SyncStatus.Failed, PortfolioErrors.SyncFailed.Message));
        (view.Trades.Status, view.Ledger!.Status).ShouldBe(("succeeded", "failed"));
        (await s.Data.IsRunningAsync(s.Account.Id, CancellationToken.None)).ShouldBeFalse();
        s.Dhan.LedgerFailure = null;
        (await s.SyncAsync(from: null, defaultFrom: true)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_run_that_cannot_be_ended_at_all_is_left_to_the_cleanup_and_the_original_failure_is_not_hidden()
    {
        var (s, run) = await RegisteredAsync();
        s.Fx.Accounts.FailReadOnlyReadsWith = new InvalidOperationException("the database went away");
        s.Data.FailReadsWith = new InvalidOperationException("still gone"); // ending the run needs to read it

        var done = await s.Execute().HandleAsync(new ExecuteBrokerHistoryCommand(s.Queue.Tickets.Single()), CancellationToken.None);

        done.Error.ShouldBe(PortfolioErrors.SyncFailed); // no exception comes out of the use case
        run.IsRunning.ShouldBeTrue();
        s.Data.FailReadsWith = null;
        s.Fx.Clock.Advance(TimeSpan.FromMinutes(5));
        await s.Fx.Recover().HandleAsync(new RecoverInterruptedSyncsCommand(s.Fx.Clock.GetUtcNow()), CancellationToken.None);
        run.Status.ShouldBe(SyncStatus.Failed);
    }
}
