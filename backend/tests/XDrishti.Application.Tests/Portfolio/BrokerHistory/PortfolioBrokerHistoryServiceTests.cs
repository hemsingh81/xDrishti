using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.BrokerHistory;
using XDrishti.Application.Tests.Platform.Services;
using XDrishti.Domain.Common;
using XDrishti.Domain.Platform.Services;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Platform.Services.ServicesWorld;

namespace XDrishti.Application.Tests.Portfolio.BrokerHistory;

/// <summary>The scheduled read as a service (ADR 0025, ADR 0018; P2-13 criterion 11): its definition and first-start seed, which accounts it handles, how it reports an expired token and the message. Run through the real scheduler path with Run now; Monday 2026-10-12, India time.</summary>
public sealed class PortfolioBrokerHistoryServiceTests : IDisposable
{
    private readonly PortfolioFixture _fx = new();
    private readonly ScriptedSync _sync = new();
    private readonly FakeSettings _settings = new();
    private readonly ServicesWorld _world;

    public PortfolioBrokerHistoryServiceTests()
    {
        _world = new ServicesWorld(
            (_, scopes) => [new PortfolioBrokerHistoryService(scopes, _settings, NullLogger<PortfolioBrokerHistoryService>.Instance)],
            provider => provider
                .Singleton<XDrishti.Application.Abstractions.Accounts.IAccountStore>(_fx.Accounts)
                .Singleton<ISyncHealthStore>(_fx.Data.Health)
                .Singleton<IHealSettings>(new XDrishti.Application.Tests.Portfolio.Heal.FixedHealSettings())
                .Singleton<ICommandHandler<SyncBrokerHistoryCommand, BrokerHistoryRunView>>(_sync));
    }

    public void Dispose() => _world.Dispose();

    private async Task<ServiceRun> RunAsync()
    {
        var started = await _world.RunNowAsync(PortfolioBrokerHistoryService.Id);
        started.IsSuccess.ShouldBeTrue();
        await _world.Runner.WhenIdleAsync();
        return _world.Data.RunsOf(PortfolioBrokerHistoryService.Id).Last();
    }

    private Task<Domain.Accounts.BrokerAccount> AccountAsync(string name, string clientId) => _fx.ConnectedAsync(name, clientId, connect: false);

    private static BrokerSyncRunView Run(Guid account, string kind, string status, BrokerSyncCounts counts, string? error = null) =>
        new(Guid.NewGuid(), account, kind, "scheduled", status, new DateOnly(2026, 10, 9), new DateOnly(2026, 10, 12), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, counts, error);

    private static BrokerHistoryRunView Done(Guid account, int trades, int entries, int rebuilt = 0, int review = 0) =>
        new(
            Run(account, "trades", "succeeded", new BrokerSyncCounts { Stored = trades, Rebuilt = rebuilt, NeedsReview = review }),
            Run(account, "ledger", "succeeded", new BrokerSyncCounts { Stored = entries }));

    // ---- the definition ----

    [Fact]
    public async Task It_is_listed_with_the_defaults_of_the_story()
    {
        var view = await _world.ViewAsync(PortfolioBrokerHistoryService.Id);

        (view.Name, view.RunsAs, view.Enabled, view.IsDefault).ShouldBe(("Portfolio trades & ledger", "xd-api", true, true));
        (view.Schedule.Type, view.Schedule.Time, view.Schedule.TradingDaysOnly).ShouldBe(("daily", "16:30", true));
        view.Schedule.Days.ShouldBe(["mon", "tue", "wed", "thu", "fri"]);
        (view.Execution.Retries, view.Execution.TimeoutMinutes, view.Execution.CatchUp, view.Execution.NotifyOnFailure).ShouldBe((0, 60, true, true));
        view.Issues.ShouldBeEmpty();
        view.NextRun.ShouldBe(At(12, 16, 30)); // the clock reads Monday 10:00
    }

    [Fact]
    public async Task A_time_before_the_session_ended_is_saved_with_a_warning()
    {
        var saved = await _world.SaveAsync(PortfolioBrokerHistoryService.Id, Svc.Input(Schedule.Daily(new TimeOnly(15, 0)), timeout: 60, catchUp: true));

        saved.IsSuccess.ShouldBeTrue();
        saved.Value.Issues.ShouldContain(i => i.Severity == "warning");
    }

    [Fact]
    public async Task The_deployment_setting_starts_the_service_paused()
    {
        _settings.Enabled = false;
        using var paused = new ServicesWorld(
            (_, scopes) => [new PortfolioBrokerHistoryService(scopes, _settings, NullLogger<PortfolioBrokerHistoryService>.Instance)],
            provider => provider.Singleton<XDrishti.Application.Abstractions.Accounts.IAccountStore>(_fx.Accounts));

        var view = await paused.ViewAsync(PortfolioBrokerHistoryService.Id);

        view.Enabled.ShouldBeFalse();
    }

    // ---- the runs ----

    [Fact]
    public async Task Without_a_connected_account_the_run_is_skipped()
    {
        var run = await RunAsync();

        (run.Status, run.Message).ShouldBe((ServiceRunStatus.Skipped, PortfolioBrokerHistoryService.NoAccountsMessage));
    }

    [Fact]
    public async Task Every_account_is_read_one_after_the_other_and_the_message_adds_up()
    {
        var one = await AccountAsync("Example One", "1000000001");
        var two = await AccountAsync("Example Two", "1000000002");
        _sync.Answers[one.Id] = Result.Success(Done(one.Id, 41, 12, rebuilt: 2, review: 1));
        _sync.Answers[two.Id] = Result.Success(Done(two.Id, 1, 0));

        var run = await RunAsync();

        (run.Status, run.Message).ShouldBe((ServiceRunStatus.Succeeded, "2 accounts read: 42 new trades, 12 new ledger entries, 2 holdings rebuilt, 1 need review"));
        _sync.Calls.Select(c => c.AccountId).ShouldBe([one.Id, two.Id], ignoreOrder: true); // one after the other, in the order the accounts are listed
        _sync.Calls.ShouldAllBe(c => c.Scheduled == false && c.From == null); // a manual Run now; a scheduled one passes true
    }

    [Fact]
    public async Task An_expired_token_is_reported_as_token_expired_the_other_accounts_are_still_read_and_the_run_fails_so_the_owner_is_told()
    {
        var one = await AccountAsync("Example One", "1000000001");
        var two = await AccountAsync("Example Two", "1000000002");
        _sync.Answers[one.Id] = Result.Failure<BrokerHistoryRunView>(PortfolioErrors.TokenExpired);
        _sync.Answers[two.Id] = Result.Success(Done(two.Id, 3, 1));

        var run = await RunAsync();

        run.Status.ShouldBe(ServiceRunStatus.Failed);
        run.Message.ShouldBe("1 account read: 3 new trades, 1 new ledger entries, 0 holdings rebuilt, 0 need review; token_expired: Example One");
        _sync.Calls.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_token_that_dhan_refused_during_the_read_is_reported_as_token_expired_too()
    {
        var one = await AccountAsync("Example One", "1000000001");
        var failed = new BrokerHistoryRunView(Run(one.Id, "trades", "failed", BrokerSyncCounts.None, PortfolioErrors.TokenRefused.Message), null);
        _sync.Answers[one.Id] = Result.Success(failed);

        var run = await RunAsync();

        (run.Status, run.Message).ShouldBe((ServiceRunStatus.Failed, "token_expired: Example One"));
    }

    [Fact]
    public async Task Another_failure_fails_the_run_with_the_account_name_and_no_dhan_text()
    {
        var one = await AccountAsync("Example One", "1000000001");
        _sync.Answers[one.Id] = Result.Success(new BrokerHistoryRunView(Run(one.Id, "trades", "failed", BrokerSyncCounts.None, "Dhan could not be reached UPSTREAM-MARKER"), null));

        var run = await RunAsync();

        (run.Status, run.Message).ShouldBe((ServiceRunStatus.Failed, "failed: Example One"));
        run.Message!.ShouldNotContain("UPSTREAM-MARKER");
    }

    [Fact]
    public async Task An_account_that_is_not_connected_is_not_touched_and_one_with_a_sync_running_is_left_for_the_next_run()
    {
        var idle = await AccountAsync("Example One", "1000000001");
        var busy = await AccountAsync("Example Two", "1000000002");
        _sync.Answers[idle.Id] = Result.Failure<BrokerHistoryRunView>(PortfolioErrors.NotConnected);
        _sync.Answers[busy.Id] = Result.Failure<BrokerHistoryRunView>(PortfolioErrors.SyncRunning);

        var run = await RunAsync();

        (run.Status, run.Message).ShouldBe((ServiceRunStatus.Succeeded, "1 left for the next run (a sync was running)"));
    }

    // ---- quiet failures (ADR 0027) ----

    private static BrokerHistoryRunView Failed(Guid account, string reason, string kind = "trades") =>
        new(Run(account, kind, "failed", BrokerSyncCounts.None with { FailureReason = reason }, "x"), null);

    [Theory]
    [InlineData("unavailable")]
    [InlineData("rate_limited")]
    [InlineData("interrupted")]
    public async Task A_read_that_failed_for_a_reason_that_may_pass_is_left_to_the_quiet_retries_and_does_not_fail_the_run(string reason)
    {
        var one = await AccountAsync("Example One", "1000000001");
        var two = await AccountAsync("Example Two", "1000000002");
        _sync.Answers[one.Id] = Result.Success(Failed(one.Id, reason));
        _sync.Answers[two.Id] = Result.Success(Done(two.Id, 3, 1));
        await _fx.Data.Health.RecordFailureAsync(one.Id, SyncHealthKind.Trades, reason, _fx.Clock.GetUtcNow(), CancellationToken.None);

        var run = await RunAsync();

        (run.Status, run.Message).ShouldBe((ServiceRunStatus.Succeeded, "1 account read: 3 new trades, 1 new ledger entries, 0 holdings rebuilt, 0 need review; retrying: Example One"));
    }

    [Fact]
    public async Task A_read_that_has_failed_on_enough_days_in_a_row_fails_the_run_again()
    {
        var one = await AccountAsync("Example One", "1000000001");
        _sync.Answers[one.Id] = Result.Success(Failed(one.Id, "unavailable"));
        await _fx.Data.Health.RecordFailureAsync(one.Id, SyncHealthKind.Trades, "unavailable", _fx.Clock.GetUtcNow().AddDays(-1), CancellationToken.None);
        await _fx.Data.Health.RecordFailureAsync(one.Id, SyncHealthKind.Trades, "unavailable", _fx.Clock.GetUtcNow(), CancellationToken.None);

        var run = await RunAsync();

        (run.Status, run.Message).ShouldBe((ServiceRunStatus.Failed, "failed: Example One"));
    }

    [Fact]
    public async Task A_failed_ledger_is_judged_by_the_ledgers_own_days()
    {
        var one = await AccountAsync("Example One", "1000000001");
        _sync.Answers[one.Id] = Result.Success(Failed(one.Id, "unavailable", kind: "ledger"));
        await _fx.Data.Health.RecordFailureAsync(one.Id, SyncHealthKind.Trades, "unavailable", _fx.Clock.GetUtcNow().AddDays(-1), CancellationToken.None); // the trades' days do not count
        await _fx.Data.Health.RecordFailureAsync(one.Id, SyncHealthKind.Trades, "unavailable", _fx.Clock.GetUtcNow(), CancellationToken.None);
        await _fx.Data.Health.RecordFailureAsync(one.Id, SyncHealthKind.Ledger, "unavailable", _fx.Clock.GetUtcNow(), CancellationToken.None);

        var run = await RunAsync();

        (run.Status, run.Message).ShouldBe((ServiceRunStatus.Succeeded, "retrying: Example One"));
    }

    [Theory]
    [InlineData("rejected")]
    [InlineData("history_too_large")]
    [InlineData("unexpected")]
    public async Task A_failure_that_will_not_pass_by_itself_still_fails_the_run(string reason)
    {
        var one = await AccountAsync("Example One", "1000000001");
        _sync.Answers[one.Id] = Result.Success(Failed(one.Id, reason));

        var run = await RunAsync();

        (run.Status, run.Message).ShouldBe((ServiceRunStatus.Failed, "failed: Example One"));
    }

    private sealed class FakeSettings : IBrokerHistorySettings
    {
        public bool Enabled { get; set; } = true;
    }

    private sealed class ScriptedSync : ICommandHandler<SyncBrokerHistoryCommand, BrokerHistoryRunView>
    {
        public Dictionary<Guid, Result<BrokerHistoryRunView>> Answers { get; } = [];

        public List<SyncBrokerHistoryCommand> Calls { get; } = [];

        public ValueTask<Result<BrokerHistoryRunView>> HandleAsync(SyncBrokerHistoryCommand command, CancellationToken cancellationToken)
        {
            Calls.Add(command);
            return ValueTask.FromResult(Answers.TryGetValue(command.AccountId, out var answer) ? answer : Result.Failure<BrokerHistoryRunView>(PortfolioErrors.NotConnected));
        }
    }
}
