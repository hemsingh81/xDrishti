using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Api.IntegrationTests.BrokerHistory;

/// <summary>
/// The rules around "Sync now" over HTTP on the real database (P2-13, ADR 0025): one sync of an account at a time with the holdings sync, what a token or a role or a date refuses, and the safeguards of the automatic rebuild of lots.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class BrokerHistoryRulesTests(ApiFactory factory) : BrokerHistoryTestBase(factory)
{
    // ---- one sync at a time ----

    [Fact]
    public async Task While_a_run_runs_a_second_sync_now_and_a_holdings_sync_are_refused_with_sync_running_and_neither_reaches_dhan()
    {
        var held = await HeldOnTheNseAsync();
        DhanHasTheBaseTrades();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dhan.BeforeTradesAnswer = async () =>
        {
            reached.TrySetResult();
            await release.Task;
        };
        var started = await Client.SyncNowAsync(held.Account.Id, Oct5, Ct);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        var holdingsRequests = HoldingRequests.Count;

        using var second = await Client.SyncNowRawAsync(held.Account.Id, Oct5, Ct);
        using var holdings = await Client.PostAsync($"/api/accounts/{held.Account.Id}/holdings/sync", Ct);
        var running = await Client.BrokerStatusAsync(held.Account.Id, Ct);
        var duplicate = await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(
            "INSERT INTO portfolio.broker_sync_runs (id, account_id, kind, trigger, from_date, to_date, status, counts, started_by, started_at) VALUES (gen_random_uuid(), @a, 'ledger', 'manual', '2026-10-05', '2026-10-12', 'running', '{}', 'x', now())",
            ("a", held.Account.Id)));
        release.SetResult();
        var status = await Client.WaitForTheRunAsync(held.Account.Id, started, Ct);

        (second.StatusCode, (await second.ReadProblemAsync(Ct)).Code).ShouldBe((HttpStatusCode.Conflict, "portfolio.sync_running"));
        (holdings.StatusCode, (await holdings.ReadProblemAsync(Ct)).Code).ShouldBe((HttpStatusCode.Conflict, "portfolio.sync_running"));
        running.Running.ShouldBeTrue();
        duplicate.ConstraintName.ShouldBe("ux_broker_sync_runs_one_running");
        HoldingRequests.Count.ShouldBe(holdingsRequests);
        TradeRequests.Count.ShouldBe(2);
        status.Trades.LastRun!.Status.ShouldBe("succeeded");
    }

    [Fact]
    public async Task While_a_holdings_sync_runs_sync_now_is_refused_with_sync_running()
    {
        var held = await HeldOnTheNseAsync();
        DhanHasTheBaseTrades();
        await ExecuteAsync(
            "INSERT INTO portfolio.holding_syncs (id, account_id, \"trigger\", requested_by, started_at, status, new_count, increased_count, decreased_count, closed_count, unchanged_count) VALUES (gen_random_uuid(), @a, 'manual', 'x', now(), 'running', 0, 0, 0, 0, 0)",
            ("a", held.Account.Id));

        using var response = await Client.SyncNowRawAsync(held.Account.Id, Oct5, Ct);

        (response.StatusCode, (await response.ReadProblemAsync(Ct)).Code).ShouldBe((HttpStatusCode.Conflict, "portfolio.sync_running"));
        TradeRequests.ShouldBeEmpty();
        (await CountAsync("SELECT count(*) FROM portfolio.broker_sync_runs")).ShouldBe(0);
    }

    [Fact]
    public async Task A_run_that_was_left_running_long_ago_does_not_block_and_is_marked_failed()
    {
        var held = await HeldOnTheNseAsync();
        DhanHasTheBaseTrades();
        await ExecuteAsync(
            "INSERT INTO portfolio.broker_sync_runs (id, account_id, kind, trigger, from_date, to_date, status, counts, started_by, started_at) VALUES (gen_random_uuid(), @a, 'trades', 'manual', '2026-10-05', '2026-10-12', 'running', '{}', 'x', '2026-10-12T01:00:00Z')",
            ("a", held.Account.Id));

        var (_, status) = await SyncAndWaitAsync(held.Account.Id);

        status.Trades.LastRun!.Status.ShouldBe("succeeded");
        (await CountAsync("SELECT count(*) FROM portfolio.broker_sync_runs WHERE status = 'failed' AND error LIKE 'The sync did not finish%'")).ShouldBe(1);
    }

    [Fact]
    public async Task A_run_still_marked_running_by_a_process_that_ended_is_failed_when_the_application_starts()
    {
        var held = await HeldOnTheNseAsync();
        await ExecuteAsync(
            "INSERT INTO portfolio.broker_sync_runs (id, account_id, kind, trigger, from_date, to_date, status, counts, started_by, started_at) VALUES (gen_random_uuid(), @a, 'ledger', 'manual', '2026-10-05', '2026-10-12', 'running', '{}', 'x', now() - interval '1 minute')",
            ("a", held.Account.Id));

        using var scope = Factory.Services.CreateScope();
        var recover = scope.ServiceProvider.GetRequiredService<XDrishti.Application.Abstractions.Messaging.ICommandHandler<XDrishti.Application.Portfolio.RecoverInterruptedSyncsCommand>>();
        (await recover.HandleAsync(new XDrishti.Application.Portfolio.RecoverInterruptedSyncsCommand(DateTimeOffset.UtcNow), Ct)).IsSuccess.ShouldBeTrue();

        (await CountAsync("SELECT count(*) FROM portfolio.broker_sync_runs WHERE status = 'failed'")).ShouldBe(1);
        (await Client.BrokerStatusAsync(held.Account.Id, Ct)).Running.ShouldBeFalse();
    }

    // ---- who may start one ----

    [Fact]
    public async Task Sync_now_is_refused_without_a_usable_connection_a_portfolio_role_a_known_account_or_a_valid_start_date()
    {
        var unconnected = await UnconnectedAccountAsync("Example Two", "1000000002");
        var noRole = await UnconnectedAccountAsync("Example Three", "1000000003", portfolioRole: false);
        var connected = await ConnectedAccountAsync("Example One", AccountsApi.ClientId);

        using var notConnected = await Client.SyncNowRawAsync(unconnected.Id, null, Ct);
        using var roleOff = await Client.SyncNowRawAsync(noRole.Id, null, Ct);
        using var unknown = await Client.SyncNowRawAsync(Guid.NewGuid(), null, Ct);
        using var future = await Client.SyncNowRawAsync(connected.Id, "2026-10-13", Ct);
        using var ancient = await Client.SyncNowRawAsync(connected.Id, "1989-12-31", Ct);

        (notConnected.StatusCode, (await notConnected.ReadProblemAsync(Ct)).Code).ShouldBe((HttpStatusCode.Conflict, "portfolio.not_connected"));
        (roleOff.StatusCode, (await roleOff.ReadProblemAsync(Ct)).Code).ShouldBe((HttpStatusCode.Conflict, "portfolio.role_off"));
        (unknown.StatusCode, (await unknown.ReadProblemAsync(Ct)).Code).ShouldBe((HttpStatusCode.NotFound, "accounts.not_found"));
        future.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await future.ReadFieldErrorsAsync(Ct)).Keys.ShouldContain("from");
        ancient.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        TradeRequests.ShouldBeEmpty();
        (await CountAsync("SELECT count(*) FROM portfolio.broker_sync_runs")).ShouldBe(0);
    }

    [Fact]
    public async Task A_token_that_expired_is_refused_with_token_expired_and_nothing_reaches_dhan()
    {
        var held = await HeldOnTheNseAsync();
        Factory.Clock.Advance(TimeSpan.FromDays(2));
        var signedIn = await Factory.SignInAsync(Ct);

        using var response = await signedIn.Client.SyncNowRawAsync(held.Account.Id, null, Ct);

        (response.StatusCode, (await response.ReadProblemAsync(Ct)).Code).ShouldBe((HttpStatusCode.Conflict, "portfolio.token_expired"));
        TradeRequests.ShouldBeEmpty();
    }

    // ---- the safeguards of the automatic rebuild ----

    [Fact]
    public async Task A_holding_with_a_hand_dated_lot_is_left_alone_and_reported_for_review_until_the_owner_rebuilds_it()
    {
        var held = await HeldOnTheNseAsync();
        DhanHasTheBaseTrades();
        var lot = (await LotsOfAsync(held.HoldingId)).Lots.Single();
        using var dated = await Client.PutJsonAsync($"/api/holdings/{held.HoldingId}/lots/{lot.Id}", new { acquiredOn = "2026-08-17" }, Ct);
        dated.StatusCode.ShouldBe(HttpStatusCode.OK);

        var (_, status) = await SyncAndWaitAsync(held.Account.Id);

        status.Trades.LastRun!.Counts.NotRebuilt.ShouldBe([new BrokerNotRebuilt("EXMPL", "needs_review")]);
        (status.HoldingsRebuilt, status.HoldingsNeedingReview).ShouldBe((0, 1));
        status.NeedsReviewSymbols.ShouldBe(["EXMPL"]);
        var lots = (await LotsOfAsync(held.HoldingId)).Lots.ShouldHaveSingleItem();
        (lots.AcquiredOn, lots.UsesDefaultDate, lots.Source).ShouldBe((new DateOnly(2026, 8, 17), false, "import"));
        (await TransactionsAsync("source = 'broker'")).ShouldBe(3); // the trades are stored all the same
        (await EventsAsync("LotsRebuilt")).ShouldBe(0);

        var rebuilt = await Client.RebuildLotsAsync(held.HoldingId, dryRun: false, Ct);

        (rebuilt.Applied, rebuilt.Rebuild.HandDatedLotsReplaced).ShouldBe((true, 1));
        (await LotsOfAsync(held.HoldingId)).Lots.Count.ShouldBe(4);
    }

    [Fact]
    public async Task A_holding_that_a_sync_or_quote_changes_while_the_rebuild_runs_is_skipped_not_retried_and_the_next_run_rebuilds_it()
    {
        var held = await HeldOnTheNseAsync();
        DhanHasTheBaseTrades();
        Factory.TradeHooks.Arm(() => ExecuteAsync("UPDATE portfolio.holdings SET qty = 31, last_price = 777 WHERE id = @id", ("id", held.HoldingId)));

        var (_, raced) = await SyncAndWaitAsync(held.Account.Id);

        Factory.TradeHooks.Armed.ShouldBeFalse();
        raced.Trades.LastRun!.Status.ShouldBe("succeeded");
        raced.Trades.LastRun.Counts.NotRebuilt.ShouldBe([new BrokerNotRebuilt("EXMPL", "changed_meanwhile")]);
        (raced.HoldingsRebuilt, raced.Trades.LastRun.Counts.Rebuilt).ShouldBe((0, 0));
        var row = await RowAsync();
        (row.Qty, row.LastPrice, row.LotCount).ShouldBe((31L, 777m, 1)); // the other change stands, the lots are as they were
        (await EventsAsync("LotsRebuilt")).ShouldBe(0);

        var (_, next) = await SyncAndWaitAsync(held.Account.Id);

        next.Trades.LastRun!.Counts.Rebuilt.ShouldBe(1);
        var lots = await LotsOfAsync(held.HoldingId);
        lots.Lots.Where(l => l.Status == "open").Sum(l => l.Qty).ShouldBe(31);
        (await EventsAsync("LotsRebuilt")).ShouldBe(1);
    }

    [Fact]
    public async Task A_lot_date_the_owner_sets_while_the_rebuild_runs_is_not_overwritten_the_rebuild_is_skipped_and_the_holding_then_needs_review()
    {
        var held = await HeldOnTheNseAsync();
        DhanHasTheBaseTrades();
        var lot = (await LotsOfAsync(held.HoldingId)).Lots.Single();
        Factory.TradeHooks.Arm(async () =>
        {
            // the owner dates the lot by hand after the rebuild has read the holding (still with its default date) and before it saves
            using var dated = await Client.PutJsonAsync($"/api/holdings/{held.HoldingId}/lots/{lot.Id}", new { acquiredOn = "2026-08-17" }, Ct);
            dated.StatusCode.ShouldBe(HttpStatusCode.OK);
        });

        var (_, raced) = await SyncAndWaitAsync(held.Account.Id);

        Factory.TradeHooks.Armed.ShouldBeFalse();
        raced.Trades.LastRun!.Counts.NotRebuilt.ShouldBe([new BrokerNotRebuilt("EXMPL", "changed_meanwhile")]);
        var kept = (await LotsOfAsync(held.HoldingId)).Lots.ShouldHaveSingleItem();
        (kept.Id, kept.AcquiredOn, kept.UsesDefaultDate).ShouldBe((lot.Id, new DateOnly(2026, 8, 17), false));
        (await EventsAsync("LotsRebuilt")).ShouldBe(0);

        var (_, next) = await SyncAndWaitAsync(held.Account.Id);

        next.Trades.LastRun!.Counts.NotRebuilt.ShouldBe([new BrokerNotRebuilt("EXMPL", "needs_review")]);
        (await LotsOfAsync(held.HoldingId)).Lots.ShouldHaveSingleItem().AcquiredOn.ShouldBe(new DateOnly(2026, 8, 17));
    }

    [Fact]
    public async Task Trades_that_do_not_fit_the_brokers_quantity_are_stored_and_the_lots_stay()
    {
        var held = await HeldOnTheNseAsync(qty: 10);
        Dhan.Trades.Add(new FakeBrokers.FakeTrade("T1", "EXMPL", "BUY", 15, 90m, D(5), Morning));

        var (_, status) = await SyncAndWaitAsync(held.Account.Id);

        status.Trades.LastRun!.Counts.NotRebuilt.ShouldBe([new BrokerNotRebuilt("EXMPL", "quantity_mismatch")]);
        (await TransactionsAsync("source = 'broker'")).ShouldBe(1);
        (await LotsOfAsync(held.HoldingId)).Lots.ShouldHaveSingleItem().Source.ShouldBe("import");
    }

    [Fact]
    public async Task A_rebuilt_holding_is_left_alone_by_the_next_run_which_stores_nothing_and_the_figures_were_dropped_by_the_rebuild()
    {
        var held = await HeldOnTheNseAsync();
        DhanHasTheBaseTrades();
        var historyBefore = await HistoryAsync();

        await SyncAndWaitAsync(held.Account.Id);
        var historyAfter = await HistoryAsync();
        var (_, again) = await SyncAndWaitAsync(held.Account.Id);

        historyBefore.FirstDate.ShouldBe(D(9));
        historyAfter.FirstDate.ShouldBe(D(5)); // the cached history was dropped by the unit of work of the rebuild
        (again.Trades.LastRun!.Counts.Stored, again.Trades.LastRun.Counts.Rebuilt, again.Trades.LastRun.Counts.Unchanged).ShouldBe((0, 0, 0)); // nothing new and nothing left to retry: the rebuild is skipped
        (await EventsAsync("LotsRebuilt")).ShouldBe(1);
    }

    [Fact]
    public async Task The_trades_of_a_security_the_account_does_not_hold_are_stored_and_counted()
    {
        var held = await HeldOnTheNseAsync();
        DhanHasTheBaseTrades();
        Dhan.Trades.Add(new FakeBrokers.FakeTrade("T7", "GONE", "BUY", 5, 50m, D(6), Morning, SecurityId: "9999"));
        Dhan.Trades.Add(new FakeBrokers.FakeTrade("T8", "GONE", "SELL", 5, 55m, D(7), Morning, SecurityId: "9999"));
        Dhan.Trades.Add(new FakeBrokers.FakeTrade("T10", "NIFTY-Oct2026-24000-CE", "BUY", 75, 120m, D(7), Morning, Segment: "NSE_FNO", Product: "MARGIN", SecurityId: "40001"));
        Dhan.Trades.Add(new FakeBrokers.FakeTrade("T11", "BSEONLY", "BUY", 7, 10m, D(7), Morning, Segment: "BSE_EQ", SecurityId: "500001"));

        var (_, status) = await SyncAndWaitAsync(held.Account.Id);

        var counts = status.Trades.LastRun!.Counts;
        (counts.Stored, counts.Skipped, counts.Rebuilt).ShouldBe((5, 2, 1));
        counts.SkippedByReason.ShouldBe(new Dictionary<string, int> { ["bse"] = 1, ["derivatives"] = 1 });
        counts.NotRebuilt.ShouldBe([new BrokerNotRebuilt("GONE", "no_holding")]);
        (await TransactionsAsync("source = 'broker'")).ShouldBe(5);
    }
}
