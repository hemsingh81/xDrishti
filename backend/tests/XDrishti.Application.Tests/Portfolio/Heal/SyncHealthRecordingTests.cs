using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Heal;
using XDrishti.Application.Tests.Portfolio.BrokerHistory;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.BrokerHistory.BrokerScenario;
using static XDrishti.Application.Tests.Portfolio.PortfolioFixture;

namespace XDrishti.Application.Tests.Portfolio.Heal;

/// <summary>
/// The persistence counters of a sync (ADR 0027): per account and kind the India days in a row whose last attempt failed, the failed attempts of the day, the reason - reset by a success - and the number of the attempt on the run. Saturday 2026-10-10 14:30 India time;
/// the trades are read from 2026-09-01.
/// </summary>
public sealed class SyncHealthRecordingTests
{
    private static readonly BrokerFailure Unavailable = new(BrokerFailureKind.Unavailable);

    private static async Task<BrokerScenario> FailingAsync(BrokerFailure? failure = null)
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Trades.AddRange(BaseTrades());
        s.Dhan.TradeFailure = (_, _, _) => failure ?? Unavailable;
        return s;
    }

    /// <summary>The next India day with a Dhan login that is valid again (a token lasts less than a day).</summary>
    private static async Task NextDayAsync(BrokerScenario s)
    {
        s.Fx.Clock.Advance(TimeSpan.FromDays(1));
        await s.Fx.Connections.ConnectByLoginAsync(s.Account);
    }

    private static SyncHealth Health(BrokerScenario s, SyncHealthKind kind = SyncHealthKind.Trades) => s.Fx.Data.Health.Of(s.Account.Id, kind);

    [Fact]
    public async Task A_failed_run_records_the_reason_the_attempt_and_the_first_failed_day()
    {
        var s = await FailingAsync();

        var run = (await s.SyncOkAsync(scheduled: true)).Trades;

        (run.Status, run.Counts.Attempts, run.Counts.FailureReason).ShouldBe(("failed", 1, "unavailable"));
        var health = Health(s);
        (health.ConsecutiveFailedDays, health.Attempts, health.LastFailureReason, health.LastFailedAt).ShouldBe((1, 1, "unavailable", s.Fx.Clock.GetUtcNow()));
        Health(s, SyncHealthKind.Ledger).IsFailing.ShouldBeFalse(); // the ledger was not attempted
    }

    [Fact]
    public async Task More_failures_on_the_same_day_add_attempts_but_not_days()
    {
        var s = await FailingAsync();
        await s.SyncOkAsync(scheduled: true);
        s.Fx.Clock.Advance(TimeSpan.FromMinutes(1));
        var second = (await s.SyncOkAsync(scheduled: true)).Trades;
        s.Fx.Clock.Advance(TimeSpan.FromMinutes(5));
        var third = (await s.SyncOkAsync(scheduled: true)).Trades;

        (second.Counts.Attempts, third.Counts.Attempts).ShouldBe((2, 3));
        var health = Health(s);
        (health.ConsecutiveFailedDays, health.Attempts).ShouldBe((1, 3));
    }

    [Fact]
    public async Task The_first_failure_of_the_next_day_is_the_second_failed_day_and_starts_the_attempts_again()
    {
        var s = await FailingAsync();
        await s.SyncOkAsync(scheduled: true);
        await NextDayAsync(s);

        var run = (await s.SyncOkAsync(scheduled: true)).Trades;

        run.Counts.Attempts.ShouldBe(1);
        var health = Health(s);
        (health.ConsecutiveFailedDays, health.Attempts).ShouldBe((2, 1));
    }

    [Fact]
    public async Task A_success_clears_the_counters_and_the_next_failure_is_a_first_failed_day_again()
    {
        var s = await FailingAsync();
        await s.SyncOkAsync(scheduled: true);
        await NextDayAsync(s);
        await s.SyncOkAsync(scheduled: true);
        Health(s).ConsecutiveFailedDays.ShouldBe(2);

        s.Dhan.TradeFailure = null;
        s.Fx.Clock.Advance(TimeSpan.FromHours(1));
        (await s.SyncOkAsync(scheduled: true)).Trades.Status.ShouldBe("succeeded");
        var healthy = Health(s);
        (healthy.ConsecutiveFailedDays, healthy.Attempts, healthy.LastFailureReason, healthy.LastFailedAt).ShouldBe((0, 0, null, null));

        s.Dhan.TradeFailure = (_, _, _) => Unavailable;
        await NextDayAsync(s);
        await s.SyncOkAsync(scheduled: true);

        Health(s).ConsecutiveFailedDays.ShouldBe(1);
    }

    [Fact]
    public async Task A_day_whose_last_attempt_succeeded_is_not_a_failed_day()
    {
        var s = await FailingAsync();
        await s.SyncOkAsync(scheduled: true); // fails at 14:30 ...
        s.Dhan.TradeFailure = null;
        s.Fx.Clock.Advance(TimeSpan.FromMinutes(1));
        await s.SyncOkAsync(scheduled: true); // ... and the retry succeeds

        Health(s).ConsecutiveFailedDays.ShouldBe(0);
    }

    [Fact]
    public async Task A_failed_ledger_is_counted_against_the_ledger_and_not_the_trades()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Trades.AddRange(BaseTrades());
        s.Dhan.LedgerFailure = (_, _) => Unavailable;

        var view = await s.SyncOkAsync(scheduled: true);

        (view.Trades.Status, view.Ledger!.Status, view.Ledger.Counts.FailureReason).ShouldBe(("succeeded", "failed", "unavailable"));
        (Health(s).IsFailing, Health(s, SyncHealthKind.Ledger).IsFailing).ShouldBe((false, true));
    }

    [Theory]
    [InlineData(BrokerFailureKind.RateLimited, "DH-904", "rate_limited")]
    [InlineData(BrokerFailureKind.Rejected, "DH-910", "rejected")]
    [InlineData(BrokerFailureKind.Unavailable, null, "unavailable")]
    public async Task The_reason_of_a_dhan_failure_is_told_apart(BrokerFailureKind kind, string? code, string reason)
    {
        var s = await FailingAsync(new BrokerFailure(kind, code));

        var run = (await s.SyncOkAsync(scheduled: true)).Trades;

        run.Counts.FailureReason.ShouldBe(reason);
        Health(s).LastFailureReason.ShouldBe(reason);
    }

    [Fact]
    public async Task A_stretch_of_dates_that_is_too_large_even_halved_is_a_permanent_failure()
    {
        var s = await FailingAsync(new BrokerFailure(BrokerFailureKind.Rejected, "DH-905"));

        var run = (await s.SyncOkAsync(scheduled: true, from: new DateOnly(2026, 10, 1))).Trades;

        run.Counts.FailureReason.ShouldBe("history_too_large");
        Health(s).LastFailureReason.ShouldBe("history_too_large");
    }

    [Fact]
    public async Task A_scheduled_run_that_finds_the_token_expired_notes_it_for_the_trades_and_the_ledger()
    {
        var s = await BrokerScenario.StartAsync();
        s.Fx.Clock.Advance(TimeSpan.FromDays(3));

        (await s.SyncAsync(scheduled: true)).Error.Code.ShouldBe("portfolio.token_expired");

        (Health(s).LastFailureReason, Health(s, SyncHealthKind.Ledger).LastFailureReason).ShouldBe(("token_expired", "token_expired"));
    }

    [Fact]
    public async Task A_holdings_refresh_that_fails_is_counted_and_a_success_clears_it()
    {
        var s = await BrokerScenario.StartAsync();
        s.Fx.Dhan.Failure = Unavailable;
        (await s.Fx.SyncAsync(s.Account.Id, SyncTrigger.Auto)).IsFailure.ShouldBeTrue();
        s.Fx.Clock.Advance(TimeSpan.FromMinutes(1));
        (await s.Fx.SyncAsync(s.Account.Id, SyncTrigger.Auto)).IsFailure.ShouldBeTrue();

        var failing = Health(s, SyncHealthKind.Holdings);

        (failing.ConsecutiveFailedDays, failing.Attempts, failing.LastFailureReason).ShouldBe((1, 2, "unavailable"));
        s.Fx.Dhan.Failure = null;
        Ok(await s.Fx.SyncAsync(s.Account.Id, SyncTrigger.Auto));
        Health(s, SyncHealthKind.Holdings).IsFailing.ShouldBeFalse();
    }

    [Fact]
    public async Task An_automatic_refresh_of_an_account_that_is_not_connected_is_noted_for_the_catch_up()
    {
        var s = await BrokerScenario.StartAsync();
        s.Fx.Clock.Advance(TimeSpan.FromDays(3));

        (await s.Fx.SyncAsync(s.Account.Id, SyncTrigger.Auto)).IsFailure.ShouldBeTrue();

        Health(s, SyncHealthKind.Holdings).LastFailureReason.ShouldBe("not_connected");
    }

    [Fact]
    public async Task A_database_that_cannot_write_the_figures_does_not_fail_the_sync()
    {
        var s = await FailingAsync();
        s.Dhan.TradeFailure = null;
        s.Fx.Data.Health.FailWritesWith = new InvalidOperationException("the database is down");

        var view = await s.SyncOkAsync(scheduled: true);

        view.Trades.Status.ShouldBe("succeeded");
    }

    [Fact]
    public async Task The_status_tells_the_attempts_the_failed_days_the_reason_and_when_the_next_retry_is_due()
    {
        var s = await FailingAsync();
        await s.SyncOkAsync(scheduled: true);

        var status = Ok(await s.Status().HandleAsync(new XDrishti.Application.Portfolio.BrokerHistory.GetBrokerHistoryStatusQuery(s.Account.Id), CancellationToken.None));

        (status.Trades.Attempts, status.Trades.ConsecutiveFailedDays, status.Trades.LastFailureReason, status.Trades.RetryAt).ShouldBe((1, 1, "unavailable", s.Fx.Clock.GetUtcNow().AddMinutes(1)));
        (status.Ledger.Attempts, status.Ledger.ConsecutiveFailedDays, status.Ledger.LastFailureReason, status.Ledger.RetryAt).ShouldBe((0, 0, null, null));
    }

    [Fact]
    public async Task The_holdings_status_tells_the_failed_days_and_the_pending_retry()
    {
        var s = await BrokerScenario.StartAsync();
        s.Fx.Dhan.Failure = Unavailable;
        await s.Fx.SyncAsync(s.Account.Id, SyncTrigger.Auto);

        var status = Ok(await s.Fx.SyncStatus().HandleAsync(new XDrishti.Application.Portfolio.GetSyncStatusQuery(s.Account.Id), CancellationToken.None));

        (status.ConsecutiveFailedDays, status.LastFailureReason, status.Attempts, status.RetryAt).ShouldBe((1, "unavailable", 1, s.Fx.Clock.GetUtcNow().AddMinutes(1)));
    }
    // ---- the bell is reconciled when an attempt ends (ADR 0030) ----

    [Theory]
    [InlineData(SyncHealthKind.Holdings, true)]
    [InlineData(SyncHealthKind.Trades, true)]
    [InlineData(SyncHealthKind.Ledger, true)]
    [InlineData(SyncHealthKind.Quotes, false)]
    public async Task The_end_of_an_attempt_of_a_synced_kind_reconciles_the_items_and_the_price_poll_does_not(SyncHealthKind kind, bool reconciles)
    {
        var s = await BrokerScenario.StartAsync();
        var attention = s.Fx.Connections.Accounts.Attention;
        var recorder = s.Fx.Connections.Accounts.HealthRecorder;
        var before = attention.Reconciles; // the connect of the scenario reconciled once already

        await recorder.FailedAsync(s.Account.Id, kind, "unavailable", TestContext.Current.CancellationToken);
        await recorder.SucceededAsync(s.Account.Id, kind, TestContext.Current.CancellationToken);

        (attention.Reconciles - before).ShouldBe(reconciles ? 2 : 0);
    }

    [Fact]
    public async Task An_interrupted_attempt_is_noted_but_does_not_reconcile()
    {
        var s = await BrokerScenario.StartAsync();
        var attention = s.Fx.Connections.Accounts.Attention;
        var before = attention.Reconciles;

        await s.Fx.Connections.Accounts.HealthRecorder.FailedAsync(s.Account.Id, SyncHealthKind.Trades, SyncFailures.Interrupted, CancellationToken.None);

        Health(s).LastFailureReason.ShouldBe("interrupted");
        attention.Reconciles.ShouldBe(before);
    }

    [Fact]
    public async Task A_reconcile_that_does_not_end_cannot_hold_up_a_failure_noted_with_a_token_that_cannot_cancel()
    {
        var s = await BrokerScenario.StartAsync();
        var attention = s.Fx.Connections.Accounts.Attention;
        var entered = new TaskCompletionSource();
        attention.Wait = async token =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        };

        var noting = s.Fx.Connections.Accounts.HealthRecorder.FailedAsync(s.Account.Id, SyncHealthKind.Trades, "unavailable", CancellationToken.None).AsTask();
        await entered.Task;
        noting.IsCompleted.ShouldBeFalse();
        s.Fx.Clock.Advance(SyncHealthRecorder.UncancellableReconcileLimit + TimeSpan.FromSeconds(1));
        await noting;

        Health(s).LastFailureReason.ShouldBe("unavailable");
    }

    [Fact]
    public async Task A_cancellation_of_the_callers_own_token_still_stops_the_reconcile_and_propagates()
    {
        var s = await BrokerScenario.StartAsync();
        using var cancel = new CancellationTokenSource();
        var entered = new TaskCompletionSource();
        s.Fx.Connections.Accounts.Attention.Wait = async token =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        };

        var noting = s.Fx.Connections.Accounts.HealthRecorder.FailedAsync(s.Account.Id, SyncHealthKind.Trades, "unavailable", cancel.Token).AsTask();
        await entered.Task;
        await cancel.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => noting);
    }
}
