using System.Net;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Platform.Services;
using XDrishti.Application.Portfolio;
using XDrishti.Application.Portfolio.Attention;
using XDrishti.Application.Portfolio.BrokerHistory;
using XDrishti.Application.Portfolio.Refresh;

namespace XDrishti.Api.IntegrationTests.BrokerHistory;

/// <summary>
/// The self-healing syncs and the attention items over HTTP and on the real database (P2-16, ADR 0027): a Dhan that does not answer is retried quietly after 1, 5 and 15 minutes and nothing is shown; the second failed day in a row raises one item with Retry now and Open and a success resolves it;
/// the items of the notices carry title, actions, since and resolved; and a renewed Dhan login catches up the missed holdings, trades and ledger by itself, in that order. Monday 2026-10-12 10:00 India time.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class SelfHealingApiTests(ApiFactory factory) : BrokerHistoryTestBase(factory)
{
    private PortfolioRefreshRunner Runner => Factory.Services.GetRequiredService<PortfolioRefreshRunner>();

    private ServiceScheduler Scheduler => Factory.Services.GetRequiredService<ServiceScheduler>();

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await Factory.ResetServicesAsync();
    }

    /// <summary>Lets the service scheduler look at an instant, as the hosted loop would (it is switched off for the suite).</summary>
    private async Task SchedulerAtAsync(DateTimeOffset instant)
    {
        Factory.Clock.Pin(instant - TimeSpan.FromMinutes(1));
        await Scheduler.TickAsync(Ct);
        Factory.Clock.Pin(instant);
        await Scheduler.TickAsync(Ct);
        await Factory.Services.GetRequiredService<ServiceRunner>().WhenIdleAsync();
        Owner = await Factory.SignInElevatedAsync(Ct);
    }

    /// <summary>When the minute-by-minute look finds each retry due: the waits are 1, 5 and 15 minutes after the failure before (the pinned clock runs on from the pin, so there is a little slack).</summary>
    private static readonly int[] RetryLooks = [90, 420, 1350];

    private static readonly System.Text.Json.JsonSerializerOptions Web = new(System.Text.Json.JsonSerializerDefaults.Web);

    private static HttpResponseMessage ServiceUnavailable() => FakeBrokers.Json(HttpStatusCode.ServiceUnavailable, "{}");

    /// <summary>What the healer tick and the end of a sync run do (ADR 0030): the items are worked out and remembered; the GET only reads them.</summary>
    private async Task ReconcileAsync()
    {
        using var scope = Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAttentionReconciler>().ReconcileAsync(Ct);
    }

    /// <summary>The bell's read after a reconciliation (a healer tick has run since the cause).</summary>
    private async Task<PortfolioNotices> NoticesAsync()
    {
        await ReconcileAsync();
        return await ReadNoticesAsync();
    }

    /// <summary>The bell's read alone: nothing reconciles.</summary>
    private async Task<PortfolioNotices> ReadNoticesAsync()
    {
        using var response = await Client.GetAsync("/api/portfolio/notices", Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        AssertNoSecret(text);
        text.ShouldNotContain("accessToken", Case.Insensitive);
        return System.Text.Json.JsonSerializer.Deserialize<PortfolioNotices>(text, Web)!;
    }

    /// <summary>The attention items of the notices (the older notices - alerts, the Action center - are not the subject here).</summary>
    private async Task<List<PortfolioNotice>> AttentionAsync() => [.. (await NoticesAsync()).Items.Where(i => i.Kind is not ("price_alert_triggered" or "actions_need_decision"))];

    /// <summary>Moves the clock and signs in again (the session does not outlive a jump of hours).</summary>
    private async Task PinAsync(DateTimeOffset instant)
    {
        Factory.Clock.Pin(instant);
        Owner = await Factory.SignInElevatedAsync(Ct);
    }

    private async Task ReconnectAsync(Guid accountId)
    {
        await Client.StartConnectAsync(accountId, Ct);
        using var callback = await Client.DhanCallbackAsync(TokenId, Ct);
        callback.Location().ShouldBe(ConnectApi.Connected(accountId));
    }

    /// <summary>The pinned clock runs on from the pin, so a time read from the API is a few seconds after it.</summary>
    private static bool IsAround(DateTimeOffset? actual, DateTimeOffset pinned) => actual is { } at && at >= pinned && at < pinned.AddMinutes(1);

    private async Task<BrokerKindStatus> TradesAsync(Guid accountId) => (await Client.BrokerStatusAsync(accountId, Ct)).Trades;

    private void DhanHasTradesFromThe9th()
    {
        Dhan.Trades.Add(new FakeBrokers.FakeTrade("T1", "EXMPL", "BUY", 4, 95m, D(9), Morning));
        Dhan.Trades.Add(new FakeBrokers.FakeTrade("T2", "EXMPL", "BUY", 1, 100m, D(12), Morning));
    }

    // ---- the items of the notices ----

    [Fact]
    public async Task An_account_that_is_not_connected_is_an_item_with_the_new_fields_and_a_route_to_the_connection_tab()
    {
        var account = await UnconnectedAccountAsync();

        var notices = await NoticesAsync();

        var item = notices.Items.ShouldHaveSingleItem();
        (item.Kind, item.Severity, item.Title, item.Resolved, item.AccountId).ShouldBe(("reconnect_dhan", "warning", "Connect to Dhan", false, account.Id));
        IsAround(item.Since, MondayMorning).ShouldBeTrue();
        item.Id.ShouldStartWith($"attention:reconnect:{account.Id:N}:missing:");
        item.Actions.ShouldBe([new AttentionAction("Connect to Dhan", "route", $"/accounts?account={account.Id}&tab=connection")]);
        item.Text.ShouldBe("Example One is not connected to Dhan.");
    }

    [Fact]
    public async Task The_read_of_the_notices_sends_no_insert_update_or_delete_and_parallel_reads_change_nothing()
    {
        await UnconnectedAccountAsync();
        await ReconcileAsync();
        var before = await ItemStateAsync();

        PortfolioNotices[] reads = [];
        var statements = await Client.StatementsDuringAsync(async () => reads = await Task.WhenAll(ReadNoticesAsync(), ReadNoticesAsync(), ReadNoticesAsync()), readsOnly: false);

        statements.ShouldContain(s => s.Contains("attention_items", StringComparison.OrdinalIgnoreCase)); // the recorder saw the read of the items ...
        statements.ShouldNotContain(s => WriteStatement.IsMatch(s)); // ... and no statement that writes
        reads.ShouldAllBe(r => r.Items.Count(i => i.Kind == "reconnect_dhan") == 1);
        (await ItemStateAsync()).ShouldBe(before); // not a row touched: same rows, same row versions
    }

    [Fact]
    public async Task A_cause_is_shown_after_the_next_healer_tick_and_not_by_the_read_alone()
    {
        var account = await UnconnectedAccountAsync();

        (await ReadNoticesAsync()).Items.ShouldBeEmpty(); // nobody has reconciled yet: the read writes nothing, so it finds nothing
        await Runner.TickAsync(Ct);

        var item = (await ReadNoticesAsync()).Items.ShouldHaveSingleItem();
        (item.Kind, item.AccountId).ShouldBe(("reconnect_dhan", account.Id));
    }

    private static readonly System.Text.RegularExpressions.Regex WriteStatement = new(@"\b(INSERT|UPDATE|DELETE)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>The ids and row versions (xmin) of the remembered items: any write to a row changes its version.</summary>
    private async Task<string> ItemStateAsync()
    {
        await using var connection = new Npgsql.NpgsqlConnection(Factory.AdminConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new Npgsql.NpgsqlCommand("SELECT coalesce(string_agg(id || ':' || xmin::text, ',' ORDER BY id), '') FROM portfolio.attention_items", connection);
        return (string)(await command.ExecuteScalarAsync(Ct))!;
    }

    [Fact]
    public async Task Connecting_the_account_resolves_the_item_and_it_stays_a_day_as_resolved()
    {
        var account = await UnconnectedAccountAsync();
        var before = (await NoticesAsync()).Items.Single();

        await PinAsync(MondayMorning.AddMinutes(5));
        await ReconnectAsync(account.Id);
        var after = (await NoticesAsync()).Items.ShouldHaveSingleItem();

        (after.Id, after.Resolved, after.Since).ShouldBe((before.Id, true, before.Since));
        await PinAsync(MondayMorning.AddHours(23));
        (await NoticesAsync()).Items.ShouldContain(i => i.Id == before.Id && i.Resolved); // still shown under Resolved
        await PinAsync(MondayMorning.AddHours(26));
        (await NoticesAsync()).Items.ShouldNotContain(i => i.Id == before.Id); // a day later it is dropped (the login of 20 hours has run out meanwhile: that is a new item)
    }

    [Fact]
    public async Task An_expired_login_is_an_error_and_its_id_follows_the_expiry()
    {
        var account = await ConnectedAccountAsync();
        await PinAsync(MondayMorning.AddHours(21)); // the fake Dhan's token lasts 20 hours
        var expired = (await NoticesAsync()).Items.Single(i => !i.Resolved);

        (expired.Severity, expired.Title, expired.Kind).ShouldBe(("error", "Reconnect Dhan", "reconnect_dhan"));
        expired.Id.ShouldContain(":expired:");
        await ReconnectAsync(account.Id);

        var items = (await NoticesAsync()).Items;

        items.Single().ShouldSatisfyAllConditions(i => i.Id.ShouldBe(expired.Id), i => i.Resolved.ShouldBeTrue());
    }

    [Fact]
    public async Task Possible_dividends_in_the_ledger_are_an_info_that_resolves_when_the_owner_decides()
    {
        var held = await HeldOnTheNseAsync();
        DhanHasALedger();
        await SyncAndWaitAsync(held.Account.Id);

        var item = (await NoticesAsync()).Items.Single(i => i.Kind == "dividends_pending");
        item.ShouldSatisfyAllConditions(
            i => i.Severity.ShouldBe("info"),
            i => i.Actions.ShouldBe([new AttentionAction("Review", "route", $"/portfolio?cash=suggestions&account={held.Account.Id}")]),
            i => i.Text.ShouldStartWith("1 credit in the ledger of Example One"));

        var entry = (await Client.SuggestionsAsync(held.Account.Id, Ct)).Items.Single();
        using var dismissed = await Client.DismissRawAsync(held.Account.Id, entry.Id, Ct);
        dismissed.EnsureSuccessStatusCode();
        var after = (await NoticesAsync()).Items.Single(i => i.Kind == "dividends_pending");

        (after.Id, after.Resolved).ShouldBe((item.Id, true));
    }

    [Fact]
    public async Task A_hand_dated_holding_is_an_item_that_opens_its_rebuild_preview_and_resolves_when_the_owner_rebuilds()
    {
        var held = await HeldOnTheNseAsync();
        DhanHasTheBaseTrades();
        var lot = (await LotsOfAsync(held.HoldingId)).Lots.Single();
        using var dated = await Client.PutJsonAsync($"/api/holdings/{held.HoldingId}/lots/{lot.Id}", new { acquiredOn = "2026-08-17" }, Ct);
        dated.StatusCode.ShouldBe(HttpStatusCode.OK);
        await SyncAndWaitAsync(held.Account.Id);

        var item = (await AttentionAsync()).ShouldHaveSingleItem();

        (item.Kind, item.Severity, item.Title, item.Resolved).ShouldBe(("holdings_need_review", "warning", "Holdings need your review", false));
        item.Text.ShouldStartWith("1 holding of Example One has lots with a date you chose");
        item.Text.ShouldContain("(EXMPL)");
        item.Actions.ShouldBe([new AttentionAction("Review", "route", $"/portfolio?holding={held.HoldingId}&section=trades&rebuild=preview")]);

        (await Client.RebuildLotsAsync(held.HoldingId, dryRun: false, Ct)).Applied.ShouldBeTrue();
        var after = (await AttentionAsync()).ShouldHaveSingleItem(); // before the next nightly run: the owner's fix is seen at once

        (after.Id, after.Resolved).ShouldBe((item.Id, true));
    }

    [Fact]
    public async Task Trades_that_do_not_fit_are_an_item_once_the_deepening_has_nothing_more_to_read()
    {
        var held = await HeldOnTheNseAsync(qty: 10);
        Dhan.Trades.Add(new FakeBrokers.FakeTrade("T1", "EXMPL", "BUY", 15, 90m, D(5), Morning));
        await SyncAndWaitAsync(held.Account.Id);
        (await AttentionAsync()).Where(i => i.Kind == "trades_unexplained").ShouldBeEmpty(); // earlier history could still fix it: the nightly run goes back first

        await ExecuteAsync("UPDATE portfolio.broker_sync_state SET deepen_exhausted = true, deepened_to = '2024-01-01' WHERE kind = 'trades'");
        var item = (await AttentionAsync()).Single(i => i.Kind == "trades_unexplained");

        (item.Severity, item.Title).ShouldBe(("warning", "Trades do not explain a holding"));
        item.Text.ShouldContain("EXMPL");
        item.Text.ShouldContain("Trades back to 2024-01-01 were read.");
        item.Actions.ShouldBe([new AttentionAction("Open trades", "route", $"/portfolio?holding={held.HoldingId}&section=trades")]);
    }

    [Fact]
    public async Task A_holding_with_no_match_in_the_instrument_master_is_an_item_that_resolves_when_it_is_matched()
    {
        var held = await HeldOnTheNseAsync();
        await ExecuteAsync("UPDATE portfolio.holdings SET instrument_id = NULL");

        var item = (await AttentionAsync()).ShouldHaveSingleItem();

        (item.Kind, item.Severity, item.Title).ShouldBe(("securities_unmatched", "warning", "Securities with no match in the instrument list"));
        item.Text.ShouldStartWith("1 holding of Example One has no match in the instrument master");
        item.Actions.ShouldBe([new AttentionAction("Open", "route", $"/portfolio?unmatched=1&account={held.Account.Id}")]);

        await ExecuteAsync("UPDATE portfolio.holdings SET instrument_id = @id", ("id", held.Instrument));
        var after = (await AttentionAsync()).ShouldHaveSingleItem();

        (after.Id, after.Resolved).ShouldBe((item.Id, true));
    }

    // ---- quiet retries ----

    [Fact]
    public async Task A_dhan_that_does_not_answer_is_retried_after_a_minute_and_nothing_is_shown()
    {
        var held = await HeldOnTheNseAsync();
        DhanHasTradesFromThe9th();
        Dhan.HistoryOverride = _ => ServiceUnavailable();

        var (_, failed) = await SyncAndWaitAsync(held.Account.Id, "2026-10-09");

        var run = failed.Trades.LastRun!;
        (run.Status, run.Counts.Attempts, run.Counts.FailureReason).ShouldBe(("failed", 1, "unavailable"));
        (failed.Trades.ConsecutiveFailedDays, failed.Trades.LastFailureReason).ShouldBe((1, "unavailable"));
        IsAround(failed.Trades.RetryAt, MondayMorning.AddMinutes(1)).ShouldBeTrue();
        (await AttentionAsync()).ShouldBeEmpty();

        Dhan.HistoryOverride = null;
        await Runner.TickAsync(Ct); // too early
        (await TradesAsync(held.Account.Id)).LastRun!.Status.ShouldBe("failed");
        await PinAsync(MondayMorning.AddSeconds(61));
        await Runner.TickAsync(Ct);

        var healed = await Client.BrokerStatusAsync(held.Account.Id, Ct);
        (healed.Trades.LastRun!.Status, healed.Trades.LastRun.Counts.Attempts, healed.Trades.ConsecutiveFailedDays, healed.Trades.LastFailureReason, healed.Trades.RetryAt).ShouldBe(("succeeded", 2, 0, null, null));
        (healed.Ledger.LastRun!.Status).ShouldBe("succeeded");
        (await TransactionsAsync("source = 'broker'")).ShouldBe(2);
        (await AttentionAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_retries_wait_one_five_and_fifteen_minutes_three_times_at_most()
    {
        var held = await HeldOnTheNseAsync();
        Dhan.HistoryOverride = _ => ServiceUnavailable();
        await SyncAndWaitAsync(held.Account.Id, "2026-10-09");

        foreach (var seconds in RetryLooks)
        {
            await PinAsync(MondayMorning.AddSeconds(seconds));
            await Runner.TickAsync(Ct);
        }

        await PinAsync(MondayMorning.AddHours(2));
        await Runner.TickAsync(Ct);

        var status = await TradesAsync(held.Account.Id);
        (status.LastRun!.Counts.Attempts, status.ConsecutiveFailedDays, status.RetryAt).ShouldBe((4, 1, null));
        TradeRequests.Count(r => r.Uri.AbsolutePath.EndsWith("/0", StringComparison.Ordinal)).ShouldBeGreaterThanOrEqualTo(4);
    }

    [Fact]
    public async Task A_refusal_that_will_not_pass_is_not_retried()
    {
        var held = await HeldOnTheNseAsync();
        Dhan.HistoryOverride = _ => FakeBrokers.Json(HttpStatusCode.BadRequest, """{"errorCode":"DH-910"}""");
        await SyncAndWaitAsync(held.Account.Id, "2026-10-09");
        var calls = TradeRequests.Count;

        await PinAsync(MondayMorning.AddMinutes(30));
        await Runner.TickAsync(Ct);

        TradeRequests.Count.ShouldBe(calls);
        (await TradesAsync(held.Account.Id)).LastFailureReason.ShouldBe("rejected");
    }

    [Fact]
    public async Task The_second_failed_day_raises_one_item_with_retry_now_and_open_and_a_success_resolves_it()
    {
        var held = await HeldOnTheNseAsync();
        DhanHasTradesFromThe9th();
        Dhan.HistoryOverride = _ => ServiceUnavailable();

        await SyncAndWaitAsync(held.Account.Id, "2026-10-09"); // Monday 10:00
        foreach (var seconds in RetryLooks)
        {
            await PinAsync(MondayMorning.AddSeconds(seconds));
            await Runner.TickAsync(Ct);
        }

        (await AttentionAsync()).ShouldBeEmpty(); // one failed day
        var tuesday = MondayMorning.AddDays(1);
        await PinAsync(tuesday);
        await ReconnectAsync(held.Account.Id);
        await SyncAndWaitAsync(held.Account.Id, "2026-10-09");
        (await TradesAsync(held.Account.Id)).ConsecutiveFailedDays.ShouldBe(2);
        (await AttentionAsync()).ShouldBeEmpty(); // the retries of Tuesday are still to come: nothing is shown
        foreach (var seconds in RetryLooks)
        {
            await PinAsync(tuesday.AddSeconds(seconds));
            await Runner.TickAsync(Ct);
        }

        var item = (await AttentionAsync()).ShouldHaveSingleItem();

        (item.Kind, item.Severity, item.Title, item.Resolved).ShouldBe(("sync_failing", "error", "Trades are not being read", false));
        item.Text.ShouldBe("The trades of Example One have not been updated for 2 days: Dhan could not be reached or gave an unexpected answer.");
        item.Id.ShouldBe($"attention:sync:{held.Account.Id:N}:trades:unavailable");
        item.Actions.ShouldBe(
        [
            new AttentionAction("Retry now", "command", $"POST /api/accounts/{held.Account.Id}/sync/broker-history"),
            new AttentionAction("Open", "route", $"/accounts?account={held.Account.Id}&tab=portfolio"),
        ]);

        Dhan.HistoryOverride = null; // the owner presses Retry now: the command is the endpoint of "Sync now"
        using var retried = await Client.PostAsync(item.Actions![0].Target["POST ".Length..], Ct);
        retried.EnsureSuccessStatusCode();
        await Client.WaitForTheRunAsync(held.Account.Id, await retried.ReadAsync<XDrishti.Application.Portfolio.BrokerHistory.BrokerHistoryRunView>(Ct), Ct);

        var resolved = (await AttentionAsync()).ShouldHaveSingleItem();
        (resolved.Id, resolved.Resolved).ShouldBe((item.Id, true));
        (await TradesAsync(held.Account.Id)).ConsecutiveFailedDays.ShouldBe(0);
    }

    // ---- catch-up after a reconnect ----

    [Fact]
    public async Task A_renewed_login_catches_up_the_missed_holdings_trades_and_ledger_by_itself_in_that_order()
    {
        var held = await HeldOnTheNseAsync();
        DhanHasTradesFromThe9th();
        Dhan.Ledger.Add(new FakeBrokers.FakeLedgerLine(D(9), "V5", "PAYBLE", "FUNDS WITHDRAWAL", 10000m, 0m, 15584.07m));
        using var saved = await Client.PutJsonAsync($"/api/accounts/{held.Account.Id}/portfolio", new { refreshMode = "daily", refreshDailyTimeIst = "16:00" }, Ct);
        saved.EnsureSuccessStatusCode();
        using var resumed = await Client.PostAsync("/api/services/portfolio-broker-history/resume", Ct);
        resumed.EnsureSuccessStatusCode();
        var tuesday = new DateTimeOffset(2026, 10, 13, 16, 0, 0, TimeSpan.FromMinutes(330)); // the login ran out at 06:00

        await SchedulerAtAsync(tuesday.AddMinutes(30)); // the nightly read of 16:30 finds the login gone
        await Runner.TickAsync(Ct); // so does the refresh of 16:00, which is due
        using var skipped = await Client.GetAsync($"/api/accounts/{held.Account.Id}/holdings/sync-status", Ct);
        (await skipped.ReadAsync<SyncStatusView>(Ct)).LastFailureReason.ShouldBe("not_connected");
        (await TradesAsync(held.Account.Id)).LastFailureReason.ShouldBe("token_expired");
        (await Client.BrokerStatusAsync(held.Account.Id, Ct)).Ledger.LastFailureReason.ShouldBe("token_expired");
        (await AttentionAsync()).ShouldHaveSingleItem().ShouldSatisfyAllConditions(i => i.Kind.ShouldBe("reconnect_dhan"), i => i.Severity.ShouldBe("error")); // one item for the login, not one per sync

        await PinAsync(tuesday.AddHours(1));
        await ReconnectAsync(held.Account.Id);
        var before = Dhan.Requests.Count;
        await Runner.TickAsync(Ct); // within the minute, without a button

        var paths = Dhan.Requests.Skip(before).Select(r => r.Uri.AbsolutePath).ToList();
        var holdings = paths.IndexOf("/v2/holdings");
        var trades = paths.FindIndex(p => p.StartsWith("/v2/trades/", StringComparison.Ordinal));
        var ledger = paths.IndexOf("/v2/ledger");
        holdings.ShouldBeGreaterThanOrEqualTo(0);
        trades.ShouldBeGreaterThan(holdings);
        ledger.ShouldBeGreaterThan(trades);
        var status = await Client.BrokerStatusAsync(held.Account.Id, Ct);
        (status.Trades.LastRun!.Status, status.Ledger.LastRun!.Status, status.Trades.LastFailureReason).ShouldBe(("succeeded", "succeeded", null));
        using var holdingsStatus = await Client.GetAsync($"/api/accounts/{held.Account.Id}/holdings/sync-status", Ct);
        (await holdingsStatus.ReadAsync<SyncStatusView>(Ct)).LastFailureReason.ShouldBeNull();
        (await AttentionAsync()).ShouldHaveSingleItem().Resolved.ShouldBeTrue(); // the item for the login resolved

        var requests = Dhan.Requests.Count;
        await Runner.TickAsync(Ct);
        Dhan.Requests.Count.ShouldBe(requests); // once
    }
}
