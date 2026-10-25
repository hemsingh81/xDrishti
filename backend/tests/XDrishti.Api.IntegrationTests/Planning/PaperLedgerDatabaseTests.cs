using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Planning;
using XDrishti.Domain.Paper;
using XDrishti.Domain.Planning;

namespace XDrishti.Api.IntegrationTests.Planning;

/// <summary>
/// Spec P10-02 on a real database: the ledger reads return the stored trades with their exits, the finished-session rule (a done row with nothing pending) holds for missing, partial, pending and empty sessions, marks read the newest stored
/// daily bar at or before the cut, the independent totals match, and the endpoints answer 404 for an unknown account.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class PaperLedgerDatabaseTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly Guid AccountId = Guid.Parse("0b6f2a9e-7a52-4d7e-9c3a-5d0a7e0b7a21");

    private static readonly DateOnly Session = new(2099, 3, 2);

    private static readonly DateTimeOffset Cutoff = new(2099, 3, 2, 3, 35, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset BeforeCutoff = new(2099, 3, 2, 2, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset AfterClose = new(2099, 3, 2, 11, 30, 0, TimeSpan.Zero);

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        await ExecuteAsync(
            connection,
            """
            INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget)
            VALUES (@id, 'probe-ledger', 'Probe ledger', 'Dhan', '1000000091', current_date, '#1A2B3C', now(), now(), false, false, false, 200)
            ON CONFLICT (id) DO NOTHING
            """,
            ("id", AccountId));
        await ExecuteAsync(connection, "INSERT INTO accounts.trading_settings (account_id, enabled, mode, capital_source, capital_amount, intraday, swing, intraday_instruments, swing_buy_instruments, swing_sell_instruments) VALUES (@id, true, 'Paper', 'Fixed', 100000, true, false, 1, 0, 0) ON CONFLICT (account_id) DO NOTHING", ("id", AccountId));
        await ExecuteAsync(connection, "INSERT INTO accounts.portfolio_settings (account_id, enabled, include_in_totals) VALUES (@id, false, true) ON CONFLICT (account_id) DO NOTHING", ("id", AccountId));
    }

    private async Task<TResult> WithAsync<TResult>(Func<IServiceProvider, Task<TResult>> work)
    {
        using var scope = Factory.Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    /// <summary>One approved ticket gives the approval row and the arming instant that every seeded entry rests on.</summary>
    private async Task<(long Plan, long[] Items, long Approval, DateTimeOffset Armed)> ApprovedArmedTicketAsync(string key)
    {
        Factory.Clock.Pin(BeforeCutoff);
        NewPlanItem Item(int rank, string symbol) => new(
            rank, TicketGroup.Primary, symbol, "BUY", "ORB", new string('a', 64), "close above 15m high", "0.8 ATR below entry", "2R", "09:30-11:30", [],
            new PlanEvidence("ORB|15m|BUY|intraday|FIXED_RR{rr=2}|*", 41, "replayed history", "cell statistics only", 0.72, 0.12, [], null, 0.7), [new NewAllocation(AccountId, "paper", 100)]);
        var plan = new NewPlan(key, Session, Cutoff, 1, new string('c', 64), "cell statistics only", true, "test", DateTimeOffset.UnixEpoch, [Item(1, "NSE:LEDA"), Item(2, "NSE:LEDB"), Item(3, "NSE:LEDC")]);
        var published = await WithAsync(p => p.GetRequiredService<IPlanStore>().PublishAsync(plan, Ct).AsTask());
        var ids = (await WithAsync(p => p.GetRequiredService<IPlanStore>().ItemsAsync(published.Id, Ct).AsTask())).OrderBy(i => i.Item.Rank).Select(i => i.Id).ToArray();
        var id = ids[0];
        (await WithAsync(p => p.GetRequiredService<ICommandHandler<MoveTicketCommand, TicketMoveOutcome>>().HandleAsync(new MoveTicketCommand(id, TicketState.Approved, "owner:test@example.com", "approved for watching"), Ct).AsTask())).IsSuccess.ShouldBeTrue();
        Factory.Clock.Pin(AfterClose.AddHours(-7));
        (await WithAsync(p => p.GetRequiredService<IPlanStore>().MoveItemAsync(new ItemMove(id, TicketState.Approved, TicketState.Armed, "system:preopen", "armed", null), Ct).AsTask())).ShouldBe(MoveStatus.Created);
        var approval = await ScalarAsync<long>("SELECT id FROM workflow.approvals WHERE item_id = @i", ("i", id));
        var armed = new DateTimeOffset(DateTime.SpecifyKind(await ScalarAsync<DateTime>("SELECT created_at FROM trading.plan_events WHERE item_id = @i AND to_state = 'armed'", ("i", id)), DateTimeKind.Utc));
        return (published.Id, ids, approval, armed);
    }

    private static PaperEntry Entry(long plan, long item, long approval, DateTimeOffset armed, string symbol, string side, int qty, decimal price, decimal stop, string key) =>
        new(plan, item, AccountId, symbol, side, "ORB", new string('a', 64), "ORB|15m|BUY|intraday|FIXED_RR{rr=2}|*", "primary", approval, "owner", 100, qty, armed, armed.AddMinutes(10), price, price, stop, null, false, key, new string('d', 64), "sim-6");

    private static PaperExit Exit(DateTimeOffset at, decimal price, decimal gross, decimal? charges, decimal? net, double r) =>
        new(at, price, "Target", 1, gross, charges, charges is null ? null : "flat-10", net, charges is null ? PaperWording.Gross : PaperWording.Net("flat-10"), r, r, charges is null ? null : r, r, charges is null ? "gross" : "net", 0.1, 1.0);

    private Task<SessionWritten> WriteAsync(long epoch, DateOnly session, string outcome, int pending, params NewPaperEntry[] entries) =>
        WithAsync(p => p.GetRequiredService<IPaperStore>().WriteSessionAsync(
            new SessionWrite(epoch, [new EpochAccount(AccountId, 100_000m, "test")], entries, [], [], new NewCaptureRun(session, AfterClose, AfterClose, outcome, entries.Length, pending, 0, true, "test")), Ct).AsTask());

    [Fact]
    public async Task The_reads_return_the_stored_trades_the_finished_sessions_and_the_independent_totals()
    {
        var (plan, items, approval, armed) = await ApprovedArmedTicketAsync("paper-ledger-1");
        var epoch = await WithAsync(p => p.GetRequiredService<IPaperStore>().EnsureFirstEpochAsync(Session, AfterClose, Ct).AsTask());
        var a = new NewPaperEntry(Entry(plan, items[0], approval, armed, "LEDA", "BUY", 60, 100.15m, 99.05m, "ORB|LEDA|BUY|2099-03-01"), "measured", Exit(armed.AddMinutes(70), 102.30m, 129.00m, 20.00m, 109.00m, 1.8));
        var b = new NewPaperEntry(Entry(plan, items[1], approval, armed, "LEDB", "SELL", 50, 200.00m, 204.00m, "ORB|LEDB|SELL|2099-03-01"), "measured", Exit(armed.AddMinutes(80), 202.00m, -100.00m, 20.00m, -120.00m, -1.1));
        var c = new NewPaperEntry(Entry(plan, items[2], approval, armed, "LEDC", "BUY", 40, 50.00m, 47.50m, "ORB|LEDC|BUY|2099-03-01"), "measured", null);
        await WriteAsync(epoch.Id, new DateOnly(2099, 4, 6), "done", 0, a, b, c);
        await WriteAsync(epoch.Id, new DateOnly(2099, 4, 7), "partial", 1);
        await WriteAsync(epoch.Id, new DateOnly(2099, 4, 8), "done", 1);
        await WriteAsync(epoch.Id, new DateOnly(2099, 4, 9), "done", 0); // an empty session still has its row
        await WriteAsync(epoch.Id, new DateOnly(2099, 4, 10), "partial", 1);
        await WriteAsync(epoch.Id, new DateOnly(2099, 4, 10), "done", 0); // the later run finished it

        var data = Factory.Services.CreateScope().ServiceProvider.GetRequiredService<IPaperLedgerData>();
        var trades = (await data.TradesAsync(epoch.Id, AccountId, Ct)).Where(t => t.Symbol.StartsWith("LED", StringComparison.Ordinal)).ToList();
        var finished = await data.FinishedSessionsAsync(new DateOnly(2099, 4, 6), Ct);
        var totals = await data.TotalsAsync(epoch.Id, AccountId, Ct);

        trades.Select(t => t.Symbol).ShouldBe(["LEDA", "LEDB", "LEDC"]);
        var figures = PaperLedger.Compute(100_000m, trades, new DateOnly(2099, 3, 2));
        (figures.Gross, figures.Charges, figures.Result, figures.Equity, figures.OpenNotional, figures.OpenRisk).ShouldBe((29.00m, 40.00m, -11.00m, (decimal?)99_989.00m, 2_000.00m, 100.00m));
        trades[0].Exit!.ChargeSheetId.ShouldBe("flat-10");
        finished.ShouldBe([new DateOnly(2099, 4, 6), new DateOnly(2099, 4, 9), new DateOnly(2099, 4, 10)], ignoreOrder: true);
        totals.ExitResult.ShouldBe(-11.00m);
        totals.EntriesWithoutExit.ShouldBe(1);
        totals.ExitBeforeEntry.ShouldBe(0);
        (await data.EntryAccountsAsync(epoch.Id, Ct)).ShouldContain(AccountId);
        (await data.EpochAsync(epoch.Id, Ct))!.Id.ShouldBe(epoch.Id);
        (await data.EpochAsync(long.MaxValue, Ct)).ShouldBeNull();

        await ResignInAsync();
        var list = await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/paper/accounts", UriKind.Relative), Ct);
        var mine = list.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("accountId").GetGuid() == AccountId);
        mine.GetProperty("equity").GetProperty("number").GetDouble().ShouldBe(99_989.00);
        mine.GetProperty("realised").GetProperty("basis").GetString().ShouldBe(PaperWording.AfterStoredSheet);
        mine.GetProperty("unrealised").GetProperty("state").GetString().ShouldBe("unavailable"); // no stored daily bar for the open symbol
        mine.GetProperty("unrealised").GetProperty("number").ValueKind.ShouldBe(JsonValueKind.Null); // never 0
        var reconcile = await Client.GetFromJsonAsync<JsonElement>(new Uri($"/api/paper/accounts/{AccountId}/reconcile", UriKind.Relative), Ct);
        reconcile.GetProperty("status").GetString().ShouldBe("ok");
        var equity = await Client.GetFromJsonAsync<JsonElement>(new Uri($"/api/paper/accounts/{AccountId}/equity", UriKind.Relative), Ct);
        equity.GetProperty("accountId").GetGuid().ShouldBe(AccountId);
    }

    [Fact]
    public async Task The_closes_are_the_newest_stored_daily_bar_at_or_before_the_cut()
    {
        var id = await SeedAsync("LEDZ");
        await using (var connection = await OpenAsync())
        {
            foreach (var (date, close, complete) in new[] { (new DateOnly(2099, 5, 4), 50.5m, true), (new DateOnly(2099, 5, 5), 51.0m, true), (new DateOnly(2099, 5, 6), 99.0m, false) })
            {
                await ExecuteAsync(
                    connection,
                    "INSERT INTO market.bars_agg (instrument_id, timeframe, ts, open, high, low, close, volume, bars, complete) VALUES (@id, '1D', @ts, @c, @c, @c, @c, 1000, 375, @complete)",
                    ("id", id), ("ts", new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.FromMinutes(330)).ToUniversalTime()), ("c", (double)close), ("complete", complete));
            }
        }

        var data = Factory.Services.CreateScope().ServiceProvider.GetRequiredService<IPaperLedgerData>();
        var onFifth = await data.ClosesAsync(["NSE:LEDZ", "NSE:NOPE"], new DateOnly(2099, 5, 5), Ct);
        var onSixth = await data.ClosesAsync(["NSE:LEDZ"], new DateOnly(2099, 5, 6), Ct);
        var before = await data.ClosesAsync(["NSE:LEDZ"], new DateOnly(2099, 5, 3), Ct);

        onFifth.Keys.ShouldBe(["NSE:LEDZ"]);
        onFifth["NSE:LEDZ"].InstrumentId.ShouldBe(id);
        onFifth["NSE:LEDZ"].Close.ShouldBe(new DailyClose(new DateOnly(2099, 5, 5), 51.0m, true));
        onSixth["NSE:LEDZ"].Close.ShouldBe(new DailyClose(new DateOnly(2099, 5, 6), 99.0m, false)); // an incomplete bar is reported as incomplete: the mark refuses it
        before.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unknown_account_is_404_and_the_account_list_carries_the_label()
    {
        await ResignInAsync();

        using var positions = await Client.GetAsync(new Uri($"/api/paper/accounts/{Guid.NewGuid()}/positions", UriKind.Relative), Ct);
        using var reconcile = await Client.GetAsync(new Uri($"/api/paper/accounts/{Guid.NewGuid()}/reconcile", UriKind.Relative), Ct);
        var accounts = await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/paper/accounts", UriKind.Relative), Ct);

        positions.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await positions.Content.ReadAsStringAsync(Ct)).ShouldContain("paper.not_found");
        reconcile.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        accounts.GetProperty("basis").GetString().ShouldBe(PaperWording.Basis);
    }
}
