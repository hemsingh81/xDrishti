using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Accounts;
using XDrishti.Application.Portfolio.Heal;
using XDrishti.Application.Tests.Portfolio.BrokerHistory;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.BrokerHistory.BrokerScenario;
using static XDrishti.Application.Tests.Portfolio.PortfolioFixture;

namespace XDrishti.Application.Tests.Portfolio.Heal;

/// <summary>
/// The self-healing looked at once a minute (ADR 0027): a sync that failed for a reason that may pass is tried again quietly after 1, 5 and 15 minutes - three times at most, inside the day - and one that failed for want of a Dhan login catches up by itself,
/// holdings first, when the login is renewed. Saturday 2026-10-10 14:30 India time; the trades are read from 2026-09-01 (two windows).
/// </summary>
public sealed class HealerTests : IAsyncLifetime
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly BrokerFailure Unavailable = new(BrokerFailureKind.Unavailable);

    private BrokerScenario _s = null!;

    public async ValueTask InitializeAsync()
    {
        _s = await BrokerScenario.StartAsync();
        _s.Dhan.Trades.AddRange(BaseTrades());
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private PortfolioHealer Healer() => new(
        _s.Fx.Accounts,
        _s.Fx.Connections.Connections,
        _s.Fx.Data.Health,
        _s.Fx.Connections.Accounts.HealthRecorder,
        _s.Fx.HealSettings,
        _s.Fx.Sync(),
        _s.Sync(),
        _s.Fx.Clock,
        NullLogger<PortfolioHealer>.Instance);

    private SyncHealth Trades => _s.Fx.Data.Health.Of(_s.Account.Id, SyncHealthKind.Trades);

    private SyncHealth Holdings => _s.Fx.Data.Health.Of(_s.Account.Id, SyncHealthKind.Holdings);

    private void Advance(TimeSpan span) => _s.Fx.Clock.Advance(span);

    private async Task<int> TickAsync() => await Healer().TickAsync(Ct);

    private int TradeRuns => _s.RunsOf(BrokerSyncKind.Trades).Count;

    // ---- quiet retries ----

    [Fact]
    public async Task A_transient_failure_is_retried_after_a_minute_and_the_retry_succeeds_without_anyone_being_told()
    {
        var s = _s;
        s.Dhan.TradeFailure = (_, _, _) => Unavailable;
        var first = await s.SyncOkAsync(scheduled: true);
        first.Trades.Counts.FailureReason.ShouldBe("unavailable");
        s.Dhan.TradeFailure = null;

        Advance(TimeSpan.FromSeconds(30));
        (await TickAsync()).ShouldBe(0); // too early
        Advance(TimeSpan.FromSeconds(31));
        (await TickAsync()).ShouldBe(1);

        var runs = s.RunsOf(BrokerSyncKind.Trades);
        runs.Select(r => (r.Status, r.Counts.Attempts)).ShouldBe([(SyncStatus.Failed, 1), (SyncStatus.Succeeded, 2)]);
        s.RunsOf(BrokerSyncKind.Ledger).ShouldHaveSingleItem().Status.ShouldBe(SyncStatus.Succeeded); // the ledger follows the trades
        Trades.ShouldBe(SyncHealth.None(s.Account.Id, SyncHealthKind.Trades) with { MapperVersion = 1 });
    }

    [Fact]
    public async Task The_retries_wait_one_then_five_then_fifteen_minutes_and_there_are_three_of_them()
    {
        var s = _s;
        s.Dhan.TradeFailure = (_, _, _) => Unavailable;
        await s.SyncOkAsync(scheduled: true); // attempt 1 fails

        Advance(TimeSpan.FromMinutes(1));
        (await TickAsync()).ShouldBe(1); // attempt 2 fails
        Advance(TimeSpan.FromMinutes(4));
        (await TickAsync()).ShouldBe(0); // five minutes have not passed since the second failure
        Advance(TimeSpan.FromMinutes(1));
        (await TickAsync()).ShouldBe(1); // attempt 3 fails
        Advance(TimeSpan.FromMinutes(14));
        (await TickAsync()).ShouldBe(0);
        Advance(TimeSpan.FromMinutes(1));
        (await TickAsync()).ShouldBe(1); // attempt 4 (the third retry) fails

        Advance(TimeSpan.FromHours(1));
        (await TickAsync()).ShouldBe(0); // no fifth attempt
        _s.RunsOf(BrokerSyncKind.Trades).Select(r => r.Counts.Attempts).ShouldBe([1, 2, 3, 4]);
        (Trades.Attempts, Trades.ConsecutiveFailedDays).ShouldBe((4, 1));
    }

    [Fact]
    public async Task A_permanent_reason_is_not_retried()
    {
        var s = _s;
        s.Dhan.TradeFailure = (_, _, _) => new BrokerFailure(BrokerFailureKind.Rejected, "DH-910");
        await s.SyncOkAsync(scheduled: true);

        Advance(TimeSpan.FromMinutes(30));
        (await TickAsync()).ShouldBe(0);

        TradeRuns.ShouldBe(1);
        Trades.LastFailureReason.ShouldBe("rejected");
    }

    [Fact]
    public async Task A_retry_is_not_made_on_the_next_day()
    {
        var s = _s;
        s.Dhan.TradeFailure = (_, _, _) => Unavailable;
        Advance(TimeSpan.FromHours(9)); // 23:30 IST
        await s.SyncOkAsync(scheduled: true);

        Advance(TimeSpan.FromMinutes(40)); // 00:10 the next day
        (await TickAsync()).ShouldBe(0);

        TradeRuns.ShouldBe(1);
    }

    [Fact]
    public async Task The_retry_of_a_failed_holdings_refresh_is_made_with_the_same_back_off()
    {
        var s = _s;
        s.Fx.Dhan.Failure = Unavailable;
        (await s.Fx.SyncAsync(s.Account.Id, SyncTrigger.Auto)).IsFailure.ShouldBeTrue();
        Holdings.ShouldBe(Holdings with { ConsecutiveFailedDays = 1, LastFailureReason = "unavailable", Attempts = 1 });
        s.Fx.Dhan.Failure = null;
        var calls = s.Fx.Dhan.Tokens.Count;

        Advance(TimeSpan.FromSeconds(61));
        (await TickAsync()).ShouldBe(1);

        s.Fx.Dhan.Tokens.Count.ShouldBe(calls + 1);
        Holdings.IsFailing.ShouldBeFalse();
        s.Fx.Data.Syncs.Select(r => r.Status).ShouldBe([SyncStatus.Succeeded, SyncStatus.Failed, SyncStatus.Succeeded]); // the first import, the failed refresh, its retry
    }

    [Fact]
    public async Task A_stuck_run_is_failed_by_the_recovery_and_retried()
    {
        var s = _s;
        var begun = await s.Pipeline().BeginAsync(s.Account, BrokerSyncKind.Trades, scheduled: true, From, Actor, Ct); // never carried out: the process died
        begun.IsSuccess.ShouldBeTrue();
        Advance(TimeSpan.FromMinutes(5));

        (await s.Fx.Recover().HandleAsync(new XDrishti.Application.Portfolio.RecoverInterruptedSyncsCommand(s.Fx.Clock.GetUtcNow()), Ct)).IsSuccess.ShouldBeTrue();

        begun.Value.Status.ShouldBe(SyncStatus.Failed);
        (Trades.LastFailureReason, Trades.Attempts).ShouldBe(("interrupted", 1));
        Advance(TimeSpan.FromSeconds(61));
        (await TickAsync()).ShouldBe(1);
        Trades.IsFailing.ShouldBeFalse();
        s.RunsOf(BrokerSyncKind.Trades).Last().Counts.Attempts.ShouldBe(2);
    }

    [Fact]
    public async Task A_run_that_ran_past_its_limit_is_failed_when_the_next_one_starts_and_noted_as_interrupted()
    {
        var s = _s;
        var stuck = await s.Pipeline().BeginAsync(s.Account, BrokerSyncKind.Trades, scheduled: true, From, Actor, Ct);
        s.Data.OtherRunInProgress = false;
        Advance(XDrishti.Application.Portfolio.BrokerHistory.BrokerHistoryPipeline.StaleAfter + TimeSpan.FromMinutes(1));

        var second = await s.Pipeline().BeginAsync(s.Account, BrokerSyncKind.Trades, scheduled: true, From, Actor, Ct);

        stuck.Value.Status.ShouldBe(SyncStatus.Failed);
        second.IsSuccess.ShouldBeTrue();
        Trades.LastFailureReason.ShouldBe("interrupted");
    }

    // ---- catch-up after a reconnect ----

    [Fact]
    public async Task When_the_login_is_renewed_the_missed_holdings_trades_and_ledger_are_read_by_themselves_in_that_order()
    {
        var s = _s;
        Advance(TimeSpan.FromDays(3)); // the token has run out
        (await s.SyncAsync(scheduled: true)).Error.Code.ShouldBe("portfolio.token_expired"); // the scheduled trade read finds out
        (await s.Fx.SyncAsync(s.Account.Id, SyncTrigger.Auto)).IsFailure.ShouldBeTrue(); // so does the holdings refresh
        (Trades.LastFailureReason, Holdings.LastFailureReason).ShouldBe(("token_expired", "not_connected"));
        (await TickAsync()).ShouldBe(0); // nothing to do while the login is gone

        Advance(TimeSpan.FromMinutes(30));
        await s.Fx.Connections.ConnectByLoginAsync(s.Account); // the owner signs in at Dhan again
        var order = new List<string>();
        s.Fx.Dhan.BeforeAnswer = () =>
        {
            order.Add("holdings");
            return Task.CompletedTask;
        };
        s.Dhan.BeforeAnswer = () =>
        {
            if (!order.Contains("trades"))
            {
                order.Add("trades");
            }

            return Task.CompletedTask;
        };
        s.Dhan.LedgerFailure = (_, _) =>
        {
            if (!order.Contains("ledger"))
            {
                order.Add("ledger");
            }

            return null;
        };

        (await TickAsync()).ShouldBe(2);

        order.ShouldBe(["holdings", "trades", "ledger"]);
        (Holdings.IsFailing, Trades.IsFailing, _s.Fx.Data.Health.Of(s.Account.Id, SyncHealthKind.Ledger).IsFailing).ShouldBe((false, false, false));
        (await TickAsync()).ShouldBe(0); // and only once
    }

    [Fact]
    public async Task A_login_renewed_before_the_failure_catches_up_nothing()
    {
        var s = _s;
        Advance(TimeSpan.FromMinutes(5));
        s.Fx.Data.Health.Rows[(s.Account.Id, SyncHealthKind.Holdings)] = SyncHealth.None(s.Account.Id, SyncHealthKind.Holdings) with
        {
            ConsecutiveFailedDays = 1,
            LastFailureReason = "not_connected",
            LastFailedAt = s.Fx.Clock.GetUtcNow(),
            Attempts = 1,
        };

        var before = s.Fx.Dhan.Tokens.Count; // the first import
        (await TickAsync()).ShouldBe(0);

        s.Fx.Dhan.Tokens.Count.ShouldBe(before);
    }

    [Fact]
    public async Task A_catch_up_only_runs_what_was_missed()
    {
        var s = _s;
        Advance(TimeSpan.FromHours(1));
        s.Fx.Data.Health.Rows[(s.Account.Id, SyncHealthKind.Holdings)] = SyncHealth.None(s.Account.Id, SyncHealthKind.Holdings) with
        {
            ConsecutiveFailedDays = 1,
            LastFailureReason = "not_connected",
            LastFailedAt = s.Fx.Clock.GetUtcNow().AddMinutes(-40),
            Attempts = 1,
        };
        s.Fx.Clock.Advance(TimeSpan.FromMinutes(20));
        await s.Fx.Connections.ConnectByLoginAsync(s.Account);
        var before = s.Fx.Dhan.Tokens.Count; // the first import

        (await TickAsync()).ShouldBe(1);

        s.Dhan.TradeCalls.ShouldBeEmpty();
        s.Fx.Dhan.Tokens.Count.ShouldBe(before + 1);
    }

    [Fact]
    public async Task A_run_that_cannot_start_is_noted_so_that_the_healing_does_not_try_every_minute()
    {
        var s = _s;
        Advance(TimeSpan.FromDays(3));
        await s.SyncAsync(scheduled: true); // token_expired
        Advance(TimeSpan.FromMinutes(30));
        await s.Fx.Connections.ConnectByLoginAsync(s.Account);
        s.Fx.Accounts.FailReadOnlyReadsWith = null;
        s.Data.OtherRunInProgress = false;

        // a login that is valid on the connection but whose token is gone from the vault: the run is refused before it begins
        s.Fx.Connections.Data.Secrets.Remove((s.Account.Id, BrokerSecretName.AccessToken));
        (await TickAsync()).ShouldBe(1);
        var calls = s.Dhan.TradeCalls.Count;

        (await TickAsync()).ShouldBe(0);

        s.Dhan.TradeCalls.Count.ShouldBe(calls);
        Trades.LastFailureReason.ShouldBe("not_connected");
        Trades.LastFailedAt.ShouldBe(s.Fx.Clock.GetUtcNow());
    }

    [Fact]
    public async Task An_account_without_the_portfolio_role_is_left_alone()
    {
        var s = _s;
        var other = await s.Fx.ConnectedAsync("Example Two", "1000000002");
        await s.Fx.Connections.Accounts.UpdateRoles().HandleAsync(new UpdateRolesCommand(other.Id, new RolesInput(null, null, new RoleToggleInput(false)), Actor), Ct);
        s.Fx.Data.Health.Rows[(other.Id, SyncHealthKind.Trades)] = SyncHealth.None(other.Id, SyncHealthKind.Trades) with
        {
            ConsecutiveFailedDays = 1,
            LastFailureReason = "unavailable",
            LastFailedAt = s.Fx.Clock.GetUtcNow().AddMinutes(-3),
            Attempts = 1,
        };

        (await TickAsync()).ShouldBe(0);
    }
}
