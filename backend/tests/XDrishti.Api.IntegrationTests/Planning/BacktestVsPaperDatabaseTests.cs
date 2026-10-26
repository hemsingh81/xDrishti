using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Paper;
using XDrishti.Domain.Planning;

namespace XDrishti.Api.IntegrationTests.Planning;

/// <summary>Spec P10-04 on a real database: the read returns the entries of the epoch with their exits and cell keys, and the endpoint answers with honest states, 404 for an unknown epoch and 422 for a draw count out of range.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class BacktestVsPaperDatabaseTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly Guid AccountId = Guid.Parse("0b6f2a9e-7a52-4d7e-9c3a-5d0a7e0b7a41");

    private static readonly DateOnly Session = new(2099, 5, 4);

    private static readonly DateTimeOffset Cutoff = new(2099, 5, 4, 3, 35, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset BeforeCutoff = new(2099, 5, 4, 2, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset AfterClose = new(2099, 5, 4, 11, 30, 0, TimeSpan.Zero);

    private const string CellA = "ORB|15m|BUY|intraday|FIXED_RR{rr=2}|*";

    private const string CellB = "ORB|15m|SELL|intraday|FIXED_RR{rr=2}|*";

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        await ExecuteAsync(
            connection,
            """
            INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget)
            VALUES (@id, 'probe-compare', 'Probe compare', 'Dhan', '1000000141', current_date, '#1A2B3C', now(), now(), false, false, false, 200)
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

    private static PaperEntry Entry(long plan, long item, long approval, DateTimeOffset armed, string cell, string symbol, string key) =>
        new(plan, item, AccountId, symbol, "BUY", "ORB", new string('a', 64), cell, "primary", approval, "owner", 100, 10, armed, armed.AddMinutes(10), 100m, 100m, 98m, null, false, key, new string('d', 64), "sim-6");

    private static PaperExit Exit(DateTimeOffset at, double r) =>
        new(at, 101m, "Target", 1, 10m, 2m, "flat-10", 8m, PaperWording.Net("flat-10"), r, r, r, r, "net", 0.1, 1.0);

    [Fact]
    public async Task The_read_returns_the_entries_with_cell_keys_and_exits_and_the_endpoint_states_the_honest_figures()
    {
        Factory.Clock.Pin(BeforeCutoff);
        NewPlanItem Item(int rank) => new(
            rank, TicketGroup.Primary, "NSE:CMP" + rank, "BUY", "ORB", new string('a', 64), "close above 15m high", "0.8 ATR below entry", "2R", "09:30-11:30", [],
            new PlanEvidence(CellA, 41, "replayed history", "cell statistics only", 0.72, 0.12, [], null, 0.7), [new NewAllocation(AccountId, "paper", 100)]);
        var plan = new NewPlan("paper-compare-1", Session, Cutoff, 1, new string('c', 64), "cell statistics only", true, "test", DateTimeOffset.UnixEpoch, [Item(1), Item(2)]);
        var published = await WithAsync(p => p.GetRequiredService<IPlanStore>().PublishAsync(plan, Ct).AsTask());
        var ids = (await WithAsync(p => p.GetRequiredService<IPlanStore>().ItemsAsync(published.Id, Ct).AsTask())).OrderBy(i => i.Item.Rank).Select(i => i.Id).ToArray();
        (await WithAsync(p => p.GetRequiredService<XDrishti.Application.Abstractions.Messaging.ICommandHandler<XDrishti.Application.Planning.MoveTicketCommand, XDrishti.Application.Planning.TicketMoveOutcome>>()
            .HandleAsync(new XDrishti.Application.Planning.MoveTicketCommand(ids[0], TicketState.Approved, "owner:test@example.com", "approved for watching"), Ct).AsTask())).IsSuccess.ShouldBeTrue();
        Factory.Clock.Pin(AfterClose.AddHours(-7));
        (await WithAsync(p => p.GetRequiredService<IPlanStore>().MoveItemAsync(new ItemMove(ids[0], TicketState.Approved, TicketState.Armed, "system:preopen", "armed", null), Ct).AsTask())).ShouldBe(MoveStatus.Created);
        var approval = await ScalarAsync<long>("SELECT id FROM workflow.approvals WHERE item_id = @i", ("i", ids[0]));
        var armed = new DateTimeOffset(DateTime.SpecifyKind(await ScalarAsync<DateTime>("SELECT created_at FROM trading.plan_events WHERE item_id = @i AND to_state = 'armed'", ("i", ids[0])), DateTimeKind.Utc));
        var epoch = await WithAsync(p => p.GetRequiredService<IPaperStore>().EnsureFirstEpochAsync(Session, AfterClose, Ct).AsTask());
        var a = new NewPaperEntry(Entry(published.Id, ids[0], approval, armed, CellA, "CMPA", "ORB|CMPA|BUY|2099-05-03"), "measured", Exit(armed.AddMinutes(70), 0.8));
        var b = new NewPaperEntry(Entry(published.Id, ids[1], approval, armed, CellB, "CMPB", "ORB|CMPB|BUY|2099-05-03"), "measured", null);
        await WithAsync(p => p.GetRequiredService<IPaperStore>().WriteSessionAsync(
            new SessionWrite(epoch.Id, [new EpochAccount(AccountId, 100_000m, "test")], [a, b], [], [], new NewCaptureRun(new DateOnly(2099, 6, 1), AfterClose, AfterClose, "done", 2, 0, 0, true, "test")), Ct).AsTask());

        var data = await WithAsync(p => p.GetRequiredService<IBacktestPaperData>().ReadAsync(epoch.Id, Ct).AsTask());
        var mine = data.Rows.Where(r => r.TradeKey.Contains("CMP", StringComparison.Ordinal)).OrderBy(r => r.EntryId).ToList();
        mine.Select(r => r.CellKey).ShouldBe([CellA, CellB]);
        mine[0].RSim.ShouldBe(0.8);
        mine[0].RBasis.ShouldBe("net");
        mine[0].ChargeSheetId.ShouldBe("flat-10");
        mine[1].ExitAt.ShouldBeNull();

        await ResignInAsync();
        var view = await Client.GetFromJsonAsync<JsonElement>(new Uri($"/api/paper/backtest-vs-paper?epoch={epoch.Id}&draws=1000", UriKind.Relative), Ct);
        view.GetProperty("epochId").GetInt64().ShouldBe(epoch.Id);
        view.GetProperty("counts").GetProperty("counted").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
        view.GetProperty("counts").GetProperty("open").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
        view.GetProperty("paper").GetProperty("mean").GetProperty("state").GetString().ShouldBe("insufficient");
        view.GetProperty("paper").GetProperty("mean").GetProperty("number").ValueKind.ShouldBe(JsonValueKind.Null);
        view.GetProperty("band").GetProperty("p05").GetProperty("state").GetString().ShouldBe("insufficient");
        view.GetProperty("criteria").GetProperty("expectancy").GetProperty("verdict").GetString().ShouldBe("not_evaluable");
        view.GetProperty("fan").GetArrayLength().ShouldBe(0);

        (await Client.GetAsync(new Uri("/api/paper/backtest-vs-paper?epoch=999999999", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Client.GetAsync(new Uri("/api/paper/backtest-vs-paper?draws=5", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }
}
