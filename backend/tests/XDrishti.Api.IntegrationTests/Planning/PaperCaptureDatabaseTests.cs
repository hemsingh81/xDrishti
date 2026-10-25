using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Paper;
using XDrishti.Application.Planning;
using XDrishti.Domain.Paper;
using XDrishti.Domain.Planning;

namespace XDrishti.Api.IntegrationTests.Planning;

/// <summary>
/// Spec P10-01 on a real database: a ticket that was approved, armed and expired is captured once (idempotent), the coverage row and the skip are stored, no ticket state moves, the database refuses an entry before arming, a quantity above the
/// allocated one, a second exit and a changed label, and the endpoints read the record with its label.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class PaperCaptureDatabaseTests(ApiFactory factory) : DataTestBase(factory)
{
    private const string OwnerActor = "owner:test@example.com";

    private static readonly Guid AccountId = Guid.Parse("0b6f2a9e-7a52-4d7e-9c3a-5d0a7e0b7a04");

    private static readonly DateOnly Session = new(2099, 1, 5);

    private static readonly DateTimeOffset Cutoff = new(2099, 1, 5, 3, 35, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset BeforeCutoff = new(2099, 1, 5, 2, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset AfterClose = new(2099, 1, 5, 11, 30, 0, TimeSpan.Zero);

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        await ExecuteAsync(
            connection,
            """
            INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget)
            VALUES (@id, 'probe-paper', 'Probe', 'Dhan', '1000000080', current_date, '#1A2B3C', now(), now(), false, false, false, 200)
            ON CONFLICT (id) DO NOTHING
            """,
            ("id", AccountId));
    }

    private async Task<TResult> WithAsync<TResult>(Func<IServiceProvider, Task<TResult>> work)
    {
        using var scope = Factory.Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    /// <summary>Publishes a plan with one ticket (allocation 100), approves it as the owner, arms it as the pre-open service and expires it as the expiry service.</summary>
    private async Task<(long Plan, long Item)> ExpiredArmedTicketAsync(string key)
    {
        Factory.Clock.Pin(BeforeCutoff);
        var item = new NewPlanItem(
            1, TicketGroup.Primary, "NSE:PAPA", "BUY", "ORB", new string('a', 64), "close above 15m high", "0.8 ATR below entry", "2R", "09:30-11:30", [],
            new PlanEvidence("ORB|15m|BUY|intraday|FIXED_RR{rr=2}|*", 41, "replayed history", "cell statistics only", 0.72, 0.12, [], null, 0.7), [new NewAllocation(AccountId, "paper", 100)]);
        var plan = new NewPlan(key, Session, Cutoff, 1, new string('c', 64), "cell statistics only", true, "test", DateTimeOffset.UnixEpoch, [item]);
        var published = await WithAsync(p => p.GetRequiredService<IPlanStore>().PublishAsync(plan, Ct).AsTask());
        var id = (await WithAsync(p => p.GetRequiredService<IPlanStore>().ItemsAsync(published.Id, Ct).AsTask())).Single().Id;
        (await WithAsync(p => p.GetRequiredService<ICommandHandler<MoveTicketCommand, TicketMoveOutcome>>().HandleAsync(new MoveTicketCommand(id, TicketState.Approved, OwnerActor, "approved for watching"), Ct).AsTask())).IsSuccess.ShouldBeTrue();
        (await WithAsync(p => p.GetRequiredService<ICommandHandler<MovePlanCommand, PlanMoveOutcome>>().HandleAsync(new MovePlanCommand(published.Id, PlanState.Approved, "system:test", "approved"), Ct).AsTask())).IsSuccess.ShouldBeTrue();
        Factory.Clock.Pin(AfterClose.AddHours(-7));
        await MoveAsync(new ItemMove(id, TicketState.Approved, TicketState.Armed, "system:preopen", "armed", null));
        Factory.Clock.Pin(AfterClose);
        await MoveAsync(new ItemMove(id, TicketState.Armed, TicketState.Expired, "system:plan-expiry", "expired", null));
        return (published.Id, id);
    }

    private async Task MoveAsync(ItemMove move) =>
        (await WithAsync(p => p.GetRequiredService<IPlanStore>().MoveItemAsync(move, Ct).AsTask())).ShouldBe(MoveStatus.Created);

    private Task<XDrishti.Domain.Common.Result<PaperCaptureOutcome>> CaptureAsync() =>
        WithAsync(p => p.GetRequiredService<ICommandHandler<CapturePaperFillsCommand, PaperCaptureOutcome>>().HandleAsync(new CapturePaperFillsCommand(null, true), Ct).AsTask());

    private async Task<string?> SqlStateAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await OpenAsync();
        try
        {
            await ExecuteAsync(connection, sql, parameters);
            return null;
        }
        catch (PostgresException failure)
        {
            return failure.SqlState;
        }
    }

    [Fact]
    public async Task An_armed_then_expired_ticket_is_captured_once_with_a_coverage_row_and_no_ticket_moves()
    {
        var (plan, item) = await ExpiredArmedTicketAsync("paper-capture-1");
        var events = await ScalarAsync<long>("SELECT count(*) FROM trading.plan_events");

        var first = await CaptureAsync();
        var runsAfterFirst = await ScalarAsync<long>("SELECT count(*) FROM paper.capture_runs");
        var second = await CaptureAsync();

        first.IsSuccess.ShouldBeTrue();
        first.Value.Seen.ShouldBeGreaterThanOrEqualTo(1);
        first.Value.SkipsWritten.ShouldBeGreaterThanOrEqualTo(1);
        (await ScalarAsync<string>("SELECT code FROM paper.skips WHERE plan_item_id = @i", ("i", item))).ShouldBe(SkipCodes.NotGradable);
        (await ScalarAsync<long>("SELECT count(*) FROM paper.skips WHERE plan_item_id = @i", ("i", item))).ShouldBe(1);
        second.Value.SkipsWritten.ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM paper.entries WHERE plan_id = @p", ("p", plan))).ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM paper.capture_runs WHERE session_date = @d", ("d", Session))).ShouldBeGreaterThanOrEqualTo(1);
        runsAfterFirst.ShouldBeGreaterThanOrEqualTo(1);
        (await ScalarAsync<long>("SELECT count(*) FROM trading.plan_events")).ShouldBe(events);
    }

    [Fact]
    public async Task The_database_refuses_an_entry_before_arming_a_quantity_above_the_allocated_one_a_second_exit_and_a_changed_label()
    {
        var (plan, item) = await ExpiredArmedTicketAsync("paper-capture-2");
        var epoch = await WithAsync(p => p.GetRequiredService<IPaperStore>().EnsureFirstEpochAsync(Session, AfterClose, Ct).AsTask());
        var approval = await ScalarAsync<long>("SELECT id FROM workflow.approvals WHERE item_id = @i", ("i", item));
        var armed = new DateTimeOffset(DateTime.SpecifyKind(await ScalarAsync<DateTime>("SELECT created_at FROM trading.plan_events WHERE item_id = @i AND to_state = 'armed'", ("i", item)), DateTimeKind.Utc));
        PaperEntry Entry(int qty, DateTimeOffset at) => new(plan, item, AccountId, "NSE:PAPA", "BUY", "ORB", new string('a', 64), "ORB|15m|BUY|intraday|FIXED_RR{rr=2}|*", "primary", approval, "owner", 100, qty, armed, at, 100.15m, 100.10m, 99m, 102.3m, false, "ORB|NSE:PAPA|Buy|2099-01-05", new string('d', 64), "sim-6");
        var exit = new PaperExit(armed.AddHours(1), 102.30m, "Target", 1, 129m, null, null, null, PaperWording.Gross, 2, 1.95, null, 1.95, "gross", 0.4, 2.1);

        Task<SessionWritten> Write(PaperEntry entry, PaperExit? exitRow) =>
            WithAsync(p => p.GetRequiredService<IPaperStore>().WriteSessionAsync(new SessionWrite(epoch.Id, [new EpochAccount(AccountId, 100_000m, "test")], [new NewPaperEntry(entry, "measured", exitRow)], [], [], new NewCaptureRun(Session, AfterClose, AfterClose, "done", 1, 0, 0, true, "test")), Ct).AsTask());

        await Should.ThrowAsync<PostgresException>(() => Write(Entry(60, armed.AddMinutes(-1)), null));
        await Should.ThrowAsync<PostgresException>(() => Write(Entry(101, armed.AddMinutes(1)), null));
        var good = await Write(Entry(60, armed.AddMinutes(10)), exit with { ExitAt = armed.AddMinutes(70) });
        var again = await Write(Entry(60, armed.AddMinutes(10)), exit with { ExitAt = armed.AddMinutes(70) });

        good.ShouldBe(new SessionWritten(1, 1, 0));
        again.ShouldBe(new SessionWritten(0, 0, 0));
        var entryId = await ScalarAsync<long>("SELECT id FROM paper.entries WHERE plan_item_id = @i", ("i", item));
        (await SqlStateAsync("INSERT INTO paper.exits (entry_id, entry_at, exit_at, exit_price, exit_reason, held_sessions, gross_pnl, cost_basis, r_exact, r_gross, r_sim, r_basis, mae_r, mfe_r) SELECT id, entry_at, entry_at, 1, 'Target', 1, 1, 'x', 1, 1, 1, 'gross', 0, 0 FROM paper.entries WHERE id = @e", ("e", entryId))).ShouldBe(PostgresErrorCodes.UniqueViolation);
        (await SqlStateAsync("UPDATE paper.entries SET basis = 'real order' WHERE id = @e", ("e", entryId))).ShouldNotBeNull();
        (await ScalarAsync<string>("SELECT basis FROM paper.entries WHERE id = @e", ("e", entryId))).ShouldBe(PaperWording.Basis);
        (await ScalarAsync<int>("SELECT qty FROM paper.entries WHERE id = @e", ("e", entryId))).ShouldBe(60);
    }

    [Fact]
    public async Task The_endpoints_read_the_record_with_its_label_and_refuse_a_bad_limit()
    {
        var (plan, item) = await ExpiredArmedTicketAsync("paper-capture-3");
        await CaptureAsync();
        await ResignInAsync();

        var runs = await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/paper/capture-runs?limit=5", UriKind.Relative), Ct);
        var entries = await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/paper/entries?limit=5", UriKind.Relative), Ct);
        using var bad = await Client.GetAsync(new Uri("/api/paper/entries?limit=0", UriKind.Relative), Ct);

        runs.GetProperty("basis").GetString().ShouldBe(PaperWording.Basis);
        runs.GetProperty("limits").GetArrayLength().ShouldBeGreaterThan(0);
        runs.GetProperty("items").GetArrayLength().ShouldBeGreaterThan(0);
        entries.GetProperty("basis").GetString().ShouldBe(PaperWording.Basis);
        bad.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        plan.ShouldBeGreaterThan(0);
        item.ShouldBeGreaterThan(0);
    }
}
