using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Accounts;
using XDrishti.Application.Portfolio;
using XDrishti.Application.Portfolio.Refresh;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.PortfolioFixture;

namespace XDrishti.Application.Tests.Portfolio;

/// <summary>
/// The automatic refresh loop on a fake clock, one tick at a time: which accounts are synced when, with the real sync handler behind it.
/// 2026-10-12 is a Monday; times are India time.
/// </summary>
public sealed class PortfolioRefreshRunnerTests
{
    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    private readonly PortfolioFixture _fx = new();

    private readonly FixedSettings _settings = new();

    // Dhan states a token valid for days, so that it does not expire while a test moves the clock from day to day.
    public PortfolioRefreshRunnerTests() => _fx.Connections.Dhan.StatedExpiry = new DateTimeOffset(2026, 10, 30, 0, 0, 0, TimeSpan.Zero);

    private IServiceScopeFactory Scopes()
    {
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(IAccountStore)).Returns(_fx.Accounts);
        provider.GetService(typeof(IHoldingSyncStore)).Returns(_fx.Data);
        provider.GetService(typeof(ICommandHandler<SyncHoldingsCommand, SyncRunView>)).Returns(_ => _fx.Sync());
        provider.GetService(typeof(ICommandHandler<RecoverInterruptedSyncsCommand>)).Returns(_ => _fx.Recover());
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(provider);
        var scopes = Substitute.For<IServiceScopeFactory>();
        scopes.CreateScope().Returns(scope);
        return scopes;
    }

    private PortfolioRefreshRunner Runner() =>
        new(Scopes(), _settings, new FixedApplicationInfo(_fx.Clock.GetUtcNow()), _fx.Clock, NullLogger<PortfolioRefreshRunner>.Instance);

    private void SetTime(int day, int hour, int minute, int second = 0) =>
        _fx.Clock.SetUtcNow(new DateTimeOffset(2026, 10, day, hour, minute, second, Ist));

    private async Task<BrokerAccount> OnAsync(PortfolioInput refresh, string name = "Example One", string clientId = "1000000001", bool connect = true)
    {
        var account = await _fx.ConnectedAsync(name, clientId, connect);
        await _fx.Connections.Accounts.UpdatePortfolio().HandleAsync(new UpdatePortfolioCommand(account.Id, refresh, Actor), CancellationToken.None);
        return account;
    }

    private static PortfolioInput Daily(string time) => new(null, null, "daily", time);

    private List<HoldingSync> Runs(Guid accountId) => [.. _fx.Data.Syncs.Where(s => s.AccountId == accountId).OrderBy(s => s.StartedAt)];

    [Fact]
    public async Task An_account_with_the_refresh_off_is_never_synced()
    {
        var account = await _fx.ConnectedAsync();
        _fx.Dhan.Rows.Add(Row());
        SetTime(12, 10, 0);

        await Runner().TickAsync(CancellationToken.None);

        _fx.Dhan.Tokens.ShouldBeEmpty();
        Runs(account.Id).ShouldBeEmpty();
    }

    [Fact]
    public async Task Daily_mode_runs_once_after_the_time_and_catches_up_when_the_time_was_missed()
    {
        var account = await OnAsync(Daily("16:00"));
        _fx.Dhan.Rows.Add(Row());
        var runner = Runner();

        SetTime(12, 15, 59);
        await runner.TickAsync(CancellationToken.None);
        Runs(account.Id).ShouldBeEmpty();

        SetTime(12, 18, 30); // the application was down at 16:00
        await runner.TickAsync(CancellationToken.None);
        SetTime(12, 18, 31);
        await runner.TickAsync(CancellationToken.None);
        SetTime(12, 23, 59);
        await runner.TickAsync(CancellationToken.None);
        Runs(account.Id).Count.ShouldBe(1);

        SetTime(13, 16, 0);
        await runner.TickAsync(CancellationToken.None);
        Runs(account.Id).Count.ShouldBe(2);
    }

    [Fact]
    public async Task The_first_daily_run_of_an_account_without_holdings_is_an_import_dated_with_the_first_import_date_and_counts_as_the_days_run()
    {
        var account = await OnAsync(new PortfolioInput(null, null, "daily", "16:00", new DateOnly(2024, 3, 3)));
        _fx.Dhan.Rows.Add(Row());
        var runner = Runner();

        SetTime(12, 16, 0);
        await runner.TickAsync(CancellationToken.None);
        SetTime(12, 16, 1);
        await runner.TickAsync(CancellationToken.None);
        SetTime(12, 20, 0);
        await runner.TickAsync(CancellationToken.None);

        var run = Runs(account.Id).ShouldHaveSingleItem();
        (run.Trigger, run.Status, run.RequestedBy).ShouldBe((SyncTrigger.Import, SyncStatus.Succeeded, PortfolioRefreshRunner.Requester));
        var lot = _fx.HoldingOf("EXMPL").Lots.Single();
        (lot.AcquiredOn, lot.UsesDefaultDate, lot.Source).ShouldBe((new DateOnly(2024, 3, 3), true, LotSource.Import));

        SetTime(13, 16, 0);
        await runner.TickAsync(CancellationToken.None);
        Runs(account.Id).Select(r => r.Trigger).ShouldBe([SyncTrigger.Import, SyncTrigger.Auto]);
    }

    [Fact]
    public async Task A_failed_first_import_is_not_retried_until_the_next_day()
    {
        var account = await OnAsync(Daily("16:00"));
        _fx.Dhan.Failure = new BrokerFailure(BrokerFailureKind.Unavailable);
        var runner = Runner();

        SetTime(12, 16, 0);
        await runner.TickAsync(CancellationToken.None);
        SetTime(12, 16, 1);
        await runner.TickAsync(CancellationToken.None);

        Runs(account.Id).ShouldHaveSingleItem().Status.ShouldBe(SyncStatus.Failed);
    }

    [Fact]
    public async Task A_manual_sync_does_not_replace_the_daily_run()
    {
        var account = await OnAsync(Daily("16:00"));
        _fx.Dhan.Rows.Add(Row());
        SetTime(12, 10, 0);
        Ok(await _fx.SyncAsync(account.Id));
        SetTime(12, 16, 0);

        await Runner().TickAsync(CancellationToken.None);

        Runs(account.Id).Select(r => r.Trigger).ShouldBe([SyncTrigger.Import, SyncTrigger.Auto]);
    }

    [Fact]
    public async Task An_account_that_is_not_connected_is_skipped_once_a_day_and_not_asked_again_that_day()
    {
        var account = await OnAsync(Daily("16:00"), connect: false);
        var runner = Runner();

        SetTime(12, 16, 0);
        await runner.TickAsync(CancellationToken.None);
        SetTime(12, 16, 1);
        await runner.TickAsync(CancellationToken.None);
        SetTime(12, 20, 0);
        await runner.TickAsync(CancellationToken.None);

        var skipped = Runs(account.Id).ShouldHaveSingleItem();
        (skipped.Status, skipped.Trigger).ShouldBe((SyncStatus.Skipped, SyncTrigger.Auto));
        _fx.Dhan.Tokens.ShouldBeEmpty();
        SetTime(13, 16, 0);
        await runner.TickAsync(CancellationToken.None);
        Runs(account.Id).Count.ShouldBe(2);
    }

    [Fact]
    public async Task An_account_without_a_bucket_to_import_to_is_skipped_once_a_day_not_asked_again_every_minute()
    {
        var account = await OnAsync(Daily("16:00"));
        _fx.Dhan.Rows.Add(Row());
        _fx.Accounts.Buckets.Single(b => b.DefaultForAccountId == account.Id).Archive(_fx.Clock.GetUtcNow(), _ => false);
        var runner = Runner();

        SetTime(12, 16, 0);
        await runner.TickAsync(CancellationToken.None);
        SetTime(12, 16, 1);
        await runner.TickAsync(CancellationToken.None);
        SetTime(12, 20, 0);
        await runner.TickAsync(CancellationToken.None);

        var skipped = Runs(account.Id).ShouldHaveSingleItem();
        (skipped.Status, skipped.Trigger).ShouldBe((SyncStatus.Skipped, SyncTrigger.Auto));
        _fx.Dhan.Tokens.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_first_import_waits_until_the_settings_are_ten_minutes_old_so_dates_chosen_in_the_same_save_are_kept()
    {
        SetTime(12, 15, 59);
        var account = await OnAsync(Daily("16:00"));
        _fx.Dhan.Rows.Add(Row());
        var runner = Runner();

        SetTime(12, 16, 0);
        await runner.TickAsync(CancellationToken.None);
        SetTime(12, 16, 8);
        await runner.TickAsync(CancellationToken.None);
        Runs(account.Id).ShouldBeEmpty();
        _fx.Dhan.Tokens.ShouldBeEmpty();

        SetTime(12, 16, 10);
        await runner.TickAsync(CancellationToken.None);
        Runs(account.Id).ShouldHaveSingleItem().Trigger.ShouldBe(SyncTrigger.Import);
    }

    [Fact]
    public async Task One_account_failing_does_not_stop_the_others()
    {
        var one = await OnAsync(Daily("09:00"), "Example One", "1000000001");
        var two = await OnAsync(Daily("09:00"), "Example Two", "1000000002");
        _fx.Dhan.Rows.Add(Row());
        _fx.Dhan.BeforeAnswer = () => _fx.Dhan.Tokens.Count == 1 ? throw new InvalidOperationException("boom") : Task.CompletedTask;
        SetTime(12, 10, 0);

        await Runner().TickAsync(CancellationToken.None);

        // The first account that is looked at hits the failure, whichever it is; the other one is still synced.
        new[] { Runs(one.Id).Single().Status, Runs(two.Id).Single().Status }.Order().ShouldBe([SyncStatus.Succeeded, SyncStatus.Failed]);
    }

    [Fact]
    public async Task A_run_in_progress_is_not_overlapped()
    {
        var account = await OnAsync(Daily("09:00"));
        _fx.Dhan.Rows.Add(Row());
        _fx.Data.Syncs.Add(HoldingSync.Start(account.Id, SyncTrigger.Manual, Actor, new DateTimeOffset(2026, 10, 12, 9, 59, 0, Ist)));
        SetTime(12, 10, 0);

        await Runner().TickAsync(CancellationToken.None);

        _fx.Dhan.Tokens.ShouldBeEmpty();
        Runs(account.Id).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Archived_accounts_and_accounts_without_the_portfolio_role_are_left_alone()
    {
        var withoutRole = await OnAsync(Daily("09:00"));
        await _fx.Connections.Accounts.UpdateRoles().HandleAsync(
            new UpdateRolesCommand(withoutRole.Id, new RolesInput(null, null, new RoleToggleInput(false)), Actor), CancellationToken.None);
        _fx.Dhan.Rows.Add(Row());
        SetTime(12, 10, 0);

        await Runner().TickAsync(CancellationToken.None);

        Runs(withoutRole.Id).ShouldBeEmpty();
    }

    [Fact]
    public async Task Starting_ends_the_runs_an_earlier_process_left_running_and_a_disabled_refresh_then_does_nothing()
    {
        var account = await OnAsync(Daily("09:00"));
        _fx.Dhan.Rows.Add(Row());
        var interrupted = HoldingSync.Start(account.Id, SyncTrigger.Manual, Actor, _fx.Clock.GetUtcNow().AddMinutes(-3));
        _fx.Data.Syncs.Add(interrupted);
        _settings.Enabled = false;
        SetTime(12, 10, 0);
        var runner = new PortfolioRefreshRunner(
            Scopes(), _settings, new FixedApplicationInfo(new DateTimeOffset(2026, 10, 12, 10, 0, 0, Ist)), _fx.Clock, NullLogger<PortfolioRefreshRunner>.Instance);

        await runner.RunAsync(CancellationToken.None);

        interrupted.Status.ShouldBe(SyncStatus.Failed);
        _fx.Dhan.Tokens.ShouldBeEmpty();
    }

    private sealed class FixedSettings : IPortfolioRefreshSettings
    {
        public bool Enabled { get; set; } = true;

        public bool AutoRefreshEnabled => Enabled;
    }

    private sealed class FixedApplicationInfo(DateTimeOffset startedAt) : IApplicationInfo
    {
        public string Name => "xd-api";

        public string Version => "test";

        public string Environment => "Testing";

        public string Instance => "test";

        public DateTimeOffset StartedAt { get; } = startedAt;
    }
}
