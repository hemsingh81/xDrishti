using System.Net;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Accounts;
using XDrishti.Application.Portfolio;
using XDrishti.Application.Portfolio.Refresh;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Api.IntegrationTests.Holdings;

/// <summary>The daily automatic refresh (ADR 0013): its settings over HTTP, the schedule driven tick by tick on the pinned clock, and the hosted service (times are India time).</summary>
[Collection(ApiTestSuite.Name)]
public sealed class HoldingsRefreshTests(ApiFactory factory) : HoldingsTestBase(factory)
{
    private PortfolioRefreshRunner Runner => Factory.Services.GetRequiredService<PortfolioRefreshRunner>();

    private async Task<AccountDetail> SetRefreshAsync(Guid accountId, object body)
    {
        using var response = await Client.PutJsonAsync($"/api/accounts/{accountId}/portfolio", body, Ct);
        return (await response.ReadAsync<PortfolioSaved>(Ct)).Account;
    }

    [Fact]
    public async Task A_new_account_has_the_refresh_off_and_the_sync_status_says_so()
    {
        var account = await ConnectedAccountAsync();

        var status = await Client.SyncStatusAsync(account.Id, Ct);

        (account.Portfolio.RefreshMode, account.Portfolio.RefreshDailyTimeIst, account.Portfolio.FirstImportDate, account.Portfolio.HasHoldings)
            .ShouldBe(("off", "16:00", new DateOnly(2025, 1, 1), false));
        (status.Mode, status.DailyTimeIst, status.FirstImportDate, status.HasHoldings, status.NextRunAt, status.Running, status.Last)
            .ShouldBe(("off", "16:00", new DateOnly(2025, 1, 1), false, (DateTimeOffset?)null, false, null));
    }

    [Fact]
    public async Task The_refresh_settings_are_validated_stored_returned_and_audited()
    {
        var account = await ConnectedAccountAsync();

        var daily = await SetRefreshAsync(account.Id, new { refreshMode = "daily", refreshDailyTimeIst = "17:45", includeInTotals = false, firstImportDate = "2024-05-06" });
        var kept = await SetRefreshAsync(account.Id, new { includeInTotals = true });
        var off = await SetRefreshAsync(account.Id, new { refreshMode = "off" });

        (daily.Portfolio.RefreshMode, daily.Portfolio.RefreshDailyTimeIst, daily.Portfolio.IncludeInTotals, daily.Portfolio.FirstImportDate).ShouldBe(("daily", "17:45", false, new DateOnly(2024, 5, 6)));
        (kept.Portfolio.RefreshMode, kept.Portfolio.RefreshDailyTimeIst, kept.Portfolio.FirstImportDate).ShouldBe(("daily", "17:45", new DateOnly(2024, 5, 6))); // left out: unchanged
        (off.Portfolio.RefreshMode, off.Portfolio.RefreshDailyTimeIst).ShouldBe(("off", "17:45"));
        (await Client.GetAccountAsync(account.Id, Ct)).Portfolio.RefreshDailyTimeIst.ShouldBe("17:45");
        var events = (await (await Client.GetAsync($"/api/accounts/{account.Id}/activity", Ct)).ReadAsync<List<AccountEventView>>(Ct))
            .Where(e => e.Event == "PortfolioRefreshSettingsChanged").Select(e => e.Detail).ToList();
        events.ShouldContain("Automatic holdings refresh: off");
        events.ShouldContain("Automatic holdings refresh: daily at 17:45 IST");
        events.ShouldContain("First import date: 2025-01-01 -> 2024-05-06");
        events.Count.ShouldBe(3);
    }

    [Theory]
    [InlineData("{\"refreshMode\":\"hourly\"}", "refreshMode")]
    [InlineData("{\"refreshMode\":\"interval\"}", "refreshMode")]
    [InlineData("{\"firstImportDate\":\"2999-01-01\"}", "firstImportDate")]
    [InlineData("{\"refreshDailyTimeIst\":\"25:00\"}", "refreshDailyTimeIst")]
    [InlineData("{\"refreshDailyTimeIst\":\"4pm\"}", "refreshDailyTimeIst")]
    public async Task Invalid_refresh_values_are_400_with_the_field_name_and_the_setting_stays(string json, string field)
    {
        var account = await ConnectedAccountAsync();

        using var response = await Client.PutAsync($"/api/accounts/{account.Id}/portfolio", new StringContent(json, System.Text.Encoding.UTF8, "application/json"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadFieldErrorsAsync(Ct)).Keys.ShouldBe([field]);
        (await Client.GetAccountAsync(account.Id, Ct)).Portfolio.RefreshMode.ShouldBe("off");
    }

    [Fact]
    public async Task An_account_can_be_created_with_the_refresh_on()
    {
        Factory.Brokers.DhanClientId = AccountsApi.ClientId;

        var account = await Client.CreateAccountAsync(AccountsApi.NewAccount(roles: new { portfolio = new { enabled = true } }, portfolio: new { refreshMode = "daily", refreshDailyTimeIst = "17:15" }), Ct);

        (account.Portfolio.RefreshMode, account.Portfolio.RefreshDailyTimeIst).ShouldBe(("daily", "17:15"));
    }

    [Fact]
    public async Task The_next_run_is_part_of_the_sync_status()
    {
        var account = await ConnectedAccountAsync();

        await SetRefreshAsync(account.Id, new { refreshMode = "daily", refreshDailyTimeIst = "16:00" });
        var daily = await Client.SyncStatusAsync(account.Id, Ct);
        await SetRefreshAsync(account.Id, new { refreshMode = "off" });
        var off = await Client.SyncStatusAsync(account.Id, Ct);

        daily.NextRunAt.ShouldBe(new DateTimeOffset(2026, 10, 12, 16, 0, 0, TimeSpan.FromMinutes(330)));
        off.NextRunAt.ShouldBeNull();
    }

    [Fact]
    public async Task Daily_mode_runs_once_after_the_time_and_catches_up_a_missed_time()
    {
        var account = await ConnectedAccountAsync();
        Dhan.Holdings.Add(Holding("ALPHA", 100, 50m, 60m, "1001", "INE000A01011"));
        await SetRefreshAsync(account.Id, new { refreshMode = "daily", refreshDailyTimeIst = "16:00" });

        await Runner.TickAsync(Ct); // 10:00: too early
        (await Factory.SyncsAsync(account.Id, Ct)).ShouldBeEmpty();
        Factory.Clock.Pin(new DateTimeOffset(2026, 10, 12, 13, 0, 0, TimeSpan.Zero)); // 18:30 IST: the time was missed
        await Runner.TickAsync(Ct);
        await Runner.TickAsync(Ct);
        var first = (await Factory.SyncsAsync(account.Id, Ct)).ShouldHaveSingleItem();
        (first.Trigger, first.RequestedBy).ShouldBe((SyncTrigger.Import, "system:portfolio-refresh")); // an account without holdings: the first run is its import
        (await Factory.HoldingAsync("ALPHA", Ct)).Lots.Single().ShouldSatisfyAllConditions(
            l => l.AcquiredOn.ShouldBe(new DateOnly(2025, 1, 1)),
            l => l.UsesDefaultDate.ShouldBeTrue());
        Factory.Clock.Pin(new DateTimeOffset(2026, 10, 13, 10, 30, 0, TimeSpan.Zero)); // next day 16:00 IST
        await Runner.TickAsync(Ct);

        var runs = await Factory.SyncsAsync(account.Id, Ct);
        runs.Select(r => r.Trigger).ShouldBe([SyncTrigger.Import, SyncTrigger.Auto]);
        (await Factory.HoldingAsync("ALPHA", Ct)).Lots.Single().AcquiredOn.ShouldBe(new DateOnly(2025, 1, 1)); // later runs do not redate
    }

    [Fact]
    public async Task An_account_that_is_not_connected_gets_one_skipped_record_per_day_and_no_dhan_call()
    {
        var account = await UnconnectedAccountAsync();
        await SetRefreshAsync(account.Id, new { refreshMode = "daily", refreshDailyTimeIst = "09:00" });

        await Runner.TickAsync(Ct);
        Factory.Clock.Advance(TimeSpan.FromMinutes(10));
        await Runner.TickAsync(Ct);
        Factory.Clock.Advance(TimeSpan.FromMinutes(10));
        await Runner.TickAsync(Ct);

        var skipped = (await Factory.SyncsAsync(account.Id, Ct)).ShouldHaveSingleItem();
        (skipped.Status, skipped.Trigger, skipped.Error).ShouldBe((SyncStatus.Skipped, SyncTrigger.Auto, "The account is not connected to Dhan."));
        HoldingRequests.ShouldBeEmpty();
        var status = await Client.SyncStatusAsync(account.Id, Ct);
        status.Last!.Status.ShouldBe("skipped");
        Factory.Clock.Pin(new DateTimeOffset(2026, 10, 13, 4, 30, 0, TimeSpan.Zero)); // the next day, same time
        await Runner.TickAsync(Ct);
        (await Factory.SyncsAsync(account.Id, Ct)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task An_automatic_run_never_closes_holdings_on_an_empty_answer_it_is_skipped_once_a_day_and_the_status_shows_why()
    {
        var account = await ConnectedAccountAsync();
        Dhan.Holdings.AddRange(
        [
            Holding("ALPHA", 100, 50m, 60m, "1001", "INE000A01011"),
            Holding("BETA", 10, 200m, 180m, "1002", "INE000B01012"),
            Holding("GAMMA", 30, 40m, 41m, "1003", "INE000G01013"),
        ]);
        await ImportAllAsync(account.Id, "2025-01-01");
        await SetRefreshAsync(account.Id, new { refreshMode = "daily", refreshDailyTimeIst = "09:00" });
        Dhan.Holdings.Clear();

        await Runner.TickAsync(Ct);
        Factory.Clock.Advance(TimeSpan.FromMinutes(6));
        await Runner.TickAsync(Ct);
        Factory.Clock.Advance(TimeSpan.FromMinutes(6));
        await Runner.TickAsync(Ct);

        var autoRuns = (await Factory.SyncsAsync(account.Id, Ct)).Where(r => r.Trigger == SyncTrigger.Auto).ToList();
        var skipped = autoRuns.ShouldHaveSingleItem();
        (skipped.Status, skipped.Error).ShouldBe((SyncStatus.Skipped, "Dhan listed 0 holdings while 3 are open in xDrishti — nothing was changed; press Sync now to confirm."));
        HoldingRequests.Count.ShouldBe(2); // the import and the one automatic look of the day
        (await Client.SummaryAsync($"?accountId={account.Id}", Ct)).OpenCount.ShouldBe(3);
        (await Factory.HoldingAsync("ALPHA", Ct)).Lots.Single().AcquiredOn.ShouldBe(new DateOnly(2025, 1, 1));
        var status = await Client.SyncStatusAsync(account.Id, Ct);
        (status.Last!.Status, status.Last.Trigger, status.Last.Error).ShouldBe(("skipped", "auto", skipped.Error));
        Factory.Clock.Advance(TimeSpan.FromMinutes(6));
        await Runner.TickAsync(Ct);
        (await Factory.SyncsAsync(account.Id, Ct)).Count(r => r.Status == SyncStatus.Skipped).ShouldBe(1);
    }

    [Fact]
    public async Task An_automatic_run_prunes_old_finished_records_but_keeps_failed_and_recent_ones_and_the_activity_index_exists()
    {
        var account = await ConnectedAccountAsync();
        Dhan.Holdings.Add(Holding("ALPHA", 100, 50m, 60m, "1001", "INE000A01011"));
        await SetRefreshAsync(account.Id, new { refreshMode = "daily", refreshDailyTimeIst = "09:00" });
        await Factory.WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO portfolio.holding_syncs (id, account_id, "trigger", requested_by, started_at, finished_at, status, new_count, increased_count, decreased_count, closed_count, unchanged_count) VALUES
                    ('00000000-0000-7000-8000-0000000000e1', {0}, 'auto', 'old', {1}, {1}, 'succeeded', 0, 0, 0, 0, 1),
                    ('00000000-0000-7000-8000-0000000000e2', {0}, 'auto', 'old', {1}, {1}, 'skipped', 0, 0, 0, 0, 0),
                    ('00000000-0000-7000-8000-0000000000e3', {0}, 'manual', 'old', {1}, {1}, 'failed', 0, 0, 0, 0, 0),
                    ('00000000-0000-7000-8000-0000000000e4', {0}, 'manual', 'recent', {2}, {2}, 'succeeded', 0, 0, 0, 0, 0)
                """,
                account.Id,
                Factory.Clock.GetUtcNow().AddDays(-100),
                Factory.Clock.GetUtcNow().AddDays(-60));
            return 0;
        });
        Factory.Clock.Advance(TimeSpan.FromMinutes(11));

        await Runner.TickAsync(Ct);

        var left = (await Factory.SyncsAsync(account.Id, Ct)).Select(s => s.RequestedBy + ":" + s.Status).ToList();
        left.ShouldNotContain("old:Succeeded");
        left.ShouldNotContain("old:Skipped");
        left.ShouldContain("old:Failed");
        left.ShouldContain("recent:Succeeded");
        left.ShouldContain("system:portfolio-refresh:Succeeded");
        var index = await Factory.WithDbAsync(db => db.Database.SqlQuery<string>($"SELECT indexdef AS \"Value\" FROM pg_indexes WHERE schemaname = 'portfolio' AND indexname = 'ix_holding_syncs_account_trigger_started'").ToListAsync(Ct));
        index.ShouldHaveSingleItem().ShouldContain("INCLUDE (status)");
    }

    [Fact]
    public async Task Accounts_with_the_refresh_off_or_the_portfolio_role_off_are_left_alone_and_a_manual_sync_is_always_allowed()
    {
        var off = await ConnectedAccountAsync();
        Dhan.Holdings.Add(Holding("ALPHA", 100, 50m, 60m, "1001", "INE000A01011"));
        var noRole = await ConnectedAccountAsync("Example Two", "1000000002");
        await SetRefreshAsync(noRole.Id, new { refreshMode = "daily", refreshDailyTimeIst = "09:00" });
        using var roleOff = await Client.PutJsonAsync($"/api/accounts/{noRole.Id}/roles", new { portfolio = new { enabled = false } }, Ct);
        roleOff.StatusCode.ShouldBe(HttpStatusCode.OK);

        await Runner.TickAsync(Ct);

        HoldingRequests.ShouldBeEmpty();
        (await Client.SyncAsync(off.Id, Ct)).Status.ShouldBe("succeeded"); // outside any schedule, by hand
    }

    [Fact]
    public async Task The_hosted_service_runs_the_due_accounts_when_the_application_starts()
    {
        var account = await ConnectedAccountAsync();
        Dhan.Holdings.Add(Holding("ALPHA", 100, 50m, 60m, "1001", "INE000A01011"));
        await SetRefreshAsync(account.Id, new { refreshMode = "daily", refreshDailyTimeIst = "09:00" });
        Factory.Clock.Advance(TimeSpan.FromMinutes(11)); // an automatic first import waits until the settings are ten minutes old
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = Factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Portfolio:AutoRefresh:Enabled", "true");
            b.ConfigureTestServices(services =>
            {
                var original = services.Single(d => d.ServiceType == typeof(ICommandHandler<SyncHoldingsCommand, SyncRunView>));
                services.Remove(original);
                services.AddScoped<ICommandHandler<SyncHoldingsCommand, SyncRunView>>(sp =>
                    new Signalling((ICommandHandler<SyncHoldingsCommand, SyncRunView>)ActivatorUtilities.CreateInstance(sp, original.ImplementationType!), done));
            });
        });
        using var client = host.CreateClient(); // starts the host and its hosted services

        await done.Task.WaitAsync(Ct);

        var run = (await Factory.SyncsAsync(account.Id, Ct)).Single();
        (run.Trigger, run.Status).ShouldBe((SyncTrigger.Import, SyncStatus.Succeeded));
        (await Factory.HoldingAsync("ALPHA", Ct)).Qty.ShouldBe(100L);
    }

    [Fact]
    public async Task Starting_the_application_ends_a_sync_an_earlier_process_left_running()
    {
        var account = await ConnectedAccountAsync();
        await Factory.WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO portfolio.holding_syncs (id, account_id, \"trigger\", requested_by, started_at, status, new_count, increased_count, decreased_count, closed_count, unchanged_count) "
                + "VALUES (gen_random_uuid(), {0}, 'manual', 'x', {1}, 'running', 0, 0, 0, 0, 0)",
                account.Id,
                DateTimeOffset.UtcNow.AddMinutes(-5)); // real time: older than this test process, which started before
            return 0;
        });
        var recovered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var restarted = Factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            var original = services.Single(d => d.ServiceType == typeof(ICommandHandler<RecoverInterruptedSyncsCommand>));
            services.Remove(original);
            services.AddScoped<ICommandHandler<RecoverInterruptedSyncsCommand>>(sp =>
                new SignallingRecover((ICommandHandler<RecoverInterruptedSyncsCommand>)ActivatorUtilities.CreateInstance(sp, original.ImplementationType!), recovered));
        }));
        using var client = restarted.CreateClient();
        await recovered.Task.WaitAsync(Ct);

        var run = (await Factory.SyncsAsync(account.Id, Ct)).Single();
        (run.Status, run.Error).ShouldBe((SyncStatus.Failed, "The sync did not finish (the application stopped or crashed while it ran)."));
    }

    private sealed class Signalling(ICommandHandler<SyncHoldingsCommand, SyncRunView> inner, TaskCompletionSource done) : ICommandHandler<SyncHoldingsCommand, SyncRunView>
    {
        public async ValueTask<XDrishti.Domain.Common.Result<SyncRunView>> HandleAsync(SyncHoldingsCommand command, CancellationToken cancellationToken)
        {
            try
            {
                return await inner.HandleAsync(command, cancellationToken);
            }
            finally
            {
                done.TrySetResult();
            }
        }
    }

    private sealed class SignallingRecover(ICommandHandler<RecoverInterruptedSyncsCommand> inner, TaskCompletionSource done) : ICommandHandler<RecoverInterruptedSyncsCommand>
    {
        public async ValueTask<XDrishti.Domain.Common.Result> HandleAsync(RecoverInterruptedSyncsCommand command, CancellationToken cancellationToken)
        {
            try
            {
                return await inner.HandleAsync(command, cancellationToken);
            }
            finally
            {
                done.TrySetResult();
            }
        }
    }
}
