using System.Net;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Platform.Services;

namespace XDrishti.Api.IntegrationTests.BrokerHistory;

/// <summary>
/// The scheduled read of trades and ledger as a service of the framework (P2-13 criterion 11, ADR 0018, ADR 0025) on the real database: seeded paused by the deployment setting, run by the scheduler at 16:30 India time on trading days,
/// reports an expired token as token_expired and still works through Run now. The scheduler is asked to look at pinned instants (its hosted loop is switched off for the suite).
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class BrokerHistoryServiceTests(ApiFactory factory) : BrokerHistoryTestBase(factory)
{
    private const string Service = "portfolio-broker-history";

    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    private ServiceScheduler Scheduler => Factory.Services.GetRequiredService<ServiceScheduler>();

    private ServiceRunner Runner => Factory.Services.GetRequiredService<ServiceRunner>();

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await Factory.ResetServicesAsync();
    }

    private static DateTimeOffset At(int day, int hour, int minute) => new(2026, 10, day, hour, minute, 0, Ist);

    private async Task TickAtAsync(DateTimeOffset instant)
    {
        Factory.Clock.Pin(instant - TimeSpan.FromMinutes(1));
        await Scheduler.TickAsync(Ct);
        Factory.Clock.Pin(instant);
        await Scheduler.TickAsync(Ct);
        await Runner.WhenIdleAsync();
        Owner = await Factory.SignInElevatedAsync(Ct);
    }

    private async Task<List<ServiceRunView>> RunsAsync()
    {
        using var response = await Client.GetAsync($"/api/services/{Service}/runs?pageSize=100", Ct);
        return (await response.ReadAsync<ServiceRunPage>(Ct)).Items.OrderBy(r => r.StartedAt).ToList();
    }

    private async Task<ServiceView> ViewAsync()
    {
        using var response = await Client.GetAsync($"/api/services/{Service}", Ct);
        return await response.ReadAsync<ServiceView>(Ct);
    }

    [Fact]
    public async Task The_service_is_listed_paused_by_the_deployment_setting_with_the_defaults_of_the_story_and_is_not_run_while_paused()
    {
        var held = await HeldOnTheNseAsync();
        DhanHasTheBaseTrades();

        await TickAtAsync(At(12, 16, 30));
        var view = await ViewAsync();

        (view.Name, view.RunsAs, view.Enabled, view.Schedule.Type, view.Schedule.Time, view.Schedule.TradingDaysOnly).ShouldBe(("Portfolio trades & ledger", "xd-api", false, "daily", "16:30", true));
        (view.Execution.Retries, view.Execution.TimeoutMinutes, view.Execution.CatchUp, view.Execution.NotifyOnFailure).ShouldBe((0, 60, true, true));
        (await RunsAsync()).ShouldBeEmpty();
        TradeRequests.ShouldBeEmpty();
        (await Client.BrokerStatusAsync(held.Account.Id, Ct)).Trades.LastRun.ShouldBeNull();
    }

    [Fact]
    public async Task At_16_30_on_a_trading_day_the_first_run_reads_from_the_first_import_date_and_rebuilds_the_lots()
    {
        var held = await HeldOnTheNseAsync();
        Dhan.Trades.Add(new FakeBrokers.FakeTrade("T1", "EXMPL", "BUY", 4, 95m, D(9), Morning));
        Dhan.Trades.Add(new FakeBrokers.FakeTrade("T2", "EXMPL", "BUY", 1, 100m, D(12), Morning));
        Dhan.Ledger.Add(new FakeBrokers.FakeLedgerLine(D(9), "V5", "PAYBLE", "FUNDS WITHDRAWAL", 10000m, 0m, 15584.07m));
        using var resumed = await Client.PostAsync($"/api/services/{Service}/resume", Ct);
        resumed.EnsureSuccessStatusCode();

        await TickAtAsync(At(12, 16, 30));

        var run = (await RunsAsync()).ShouldHaveSingleItem();
        (run.Status, run.Trigger, run.Message).ShouldBe(("succeeded", "scheduled", "1 account read: 2 new trades, 1 new ledger entries, 1 holdings rebuilt, 0 need review"));
        var status = await Client.BrokerStatusAsync(held.Account.Id, Ct);
        (status.Trades.LastRun!.Trigger, status.Trades.LastRun.From, status.Trades.LastRun.To, status.Ledger.LastRun!.Trigger).ShouldBe(("first", D(9), D(12), "first"));
        (await TransactionsAsync("source = 'broker'")).ShouldBe(2);
        (await LotsOfAsync(held.HoldingId)).Lots.Count(l => l.Source == "trade").ShouldBe(2);
        (await CountAsync("SELECT count(*) FROM accounts.account_events WHERE event = 'LotsRebuilt' AND detail LIKE '%trigger broker_sync'")).ShouldBe(1);
    }

    [Fact]
    public async Task The_next_trading_day_the_run_is_incremental_and_a_day_without_news_leaves_no_audit_event()
    {
        var held = await HeldOnTheNseAsync();
        Dhan.Trades.Add(new FakeBrokers.FakeTrade("T1", "EXMPL", "BUY", 4, 95m, D(9), Morning));
        using var resumed = await Client.PostAsync($"/api/services/{Service}/resume", Ct);
        resumed.EnsureSuccessStatusCode();
        await TickAtAsync(At(12, 16, 30));
        var events = await EventsAsync("BrokerHistorySynced");
        Dhan.ClearRequests();
        Factory.Clock.Pin(At(13, 10, 0)); // Dhan's token lasts a day: the owner signs in to Dhan again on Tuesday morning
        Owner = await Factory.SignInElevatedAsync(Ct);
        await Client.StartConnectAsync(held.Account.Id, Ct);
        using var callback = await Client.DhanCallbackAsync(TokenId, Ct);
        callback.Location().ShouldBe(ConnectApi.Connected(held.Account.Id));

        await TickAtAsync(At(13, 16, 30));

        var runs = await RunsAsync();
        runs.Select(r => r.Status).ShouldBe(["succeeded", "succeeded"]);
        TradeRequests.Select(r => r.Uri.AbsolutePath).ShouldBe(["/v2/trades/2026-10-12/2026-10-13/0"]);
        (await Client.BrokerStatusAsync(held.Account.Id, Ct)).Trades.LastRun!.Trigger.ShouldBe("scheduled");
        (await EventsAsync("BrokerHistorySynced")).ShouldBe(events);
    }

    [Fact]
    public async Task A_token_that_has_expired_is_reported_as_token_expired_the_run_fails_and_nothing_is_read()
    {
        var held = await HeldOnTheNseAsync();
        DhanHasTheBaseTrades();
        using var resumed = await Client.PostAsync($"/api/services/{Service}/resume", Ct);
        resumed.EnsureSuccessStatusCode();

        await TickAtAsync(At(14, 16, 30)); // Wednesday: the token Dhan gave on Monday lasted 20 hours

        var run = (await RunsAsync()).ShouldHaveSingleItem();
        (run.Status, run.Message).ShouldBe(("failed", "token_expired: Example One"));
        TradeRequests.ShouldBeEmpty();
        var last = (await Client.BrokerStatusAsync(held.Account.Id, Ct)).Trades.LastRun!;
        (last.Status, last.Trigger, last.Error).ShouldBe(("skipped", "scheduled", "portfolio.token_expired"));
    }

    [Fact]
    public async Task Run_now_reads_every_connected_account_even_while_the_service_is_paused()
    {
        var held = await HeldOnTheNseAsync();
        DhanHasTheBaseTrades();
        DhanHasALedger();

        var run = await Factory.RunNowAsync(Client, Service, Ct);

        run.Status.ShouldBe("succeeded");
        run.Message.ShouldNotBeNull().ShouldStartWith("1 account read:");
        var status = await Client.BrokerStatusAsync(held.Account.Id, Ct);
        status.Trades.LastRun!.Trigger.ShouldBe("manual");
        status.Trades.LastRun.StartedAt.ShouldBeGreaterThan(DateTimeOffset.MinValue);
    }

    [Fact]
    public async Task An_account_without_the_portfolio_role_or_a_connection_is_not_touched()
    {
        await UnconnectedAccountAsync("Example Two", "1000000002");
        await UnconnectedAccountAsync("Example Three", "1000000003", portfolioRole: false);
        DhanHasTheBaseTrades();

        using var response = await Client.PostAsync($"/api/services/{Service}/run", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await Runner.WhenIdleAsync();

        var run = (await RunsAsync()).ShouldHaveSingleItem();
        (run.Status, run.Message).ShouldBe(("skipped", "No Portfolio account is connected to Dhan"));
        TradeRequests.ShouldBeEmpty();
    }
}
