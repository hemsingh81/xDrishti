using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Shadow;
using XDrishti.Application.Planning;
using XDrishti.Application.Shadow;
using XDrishti.Domain.Paper;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Shadow;

namespace XDrishti.Api.IntegrationTests.Planning;

/// <summary>
/// CF-08 part A on the real database, through the real handler and store: every candidate of a stored plan is written once (criterion 1), a session that has opened is a gap and never filled in (3), the chain recomputed from the stored rows matches and one changed value
/// breaks it from that session on (4), P10's own reads are unchanged by shadow rows (6), and with the switch OFF nothing is written (11). The application clock is pinned; the database clock is real, so the plans are for 2099 and the gap is for a session of 2026-10.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class PaperShadowDatabaseTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly DateOnly Monday = new(2099, 1, 5);

    private static readonly DateOnly Tuesday = new(2099, 1, 6);

    private const string Cell = "ORB|15m|BUY|intraday|FIXED_RR{rr=2}|*";

    private static NewPlanItem Item(int rank, TicketGroup group, string symbol, bool reachedFill = true) =>
        new(
            rank, group, symbol, "BUY", "ORB", new string('a', 64), "close above 15m high", "0.8 ATR below entry", "2R", "09:30-11:30", group == TicketGroup.Primary ? [] : ["not_taken"],
            new PlanEvidence(Cell, 41, "replayed history", "cell statistics only", 0.72, 0.12, reachedFill ? [] : ["portfolio_fill", "checklist", "smart_rules", "allocation"], null, 0.7), [],
            TicketLevels.Create("BUY", 100m, Monday, 98m, StopAnchor.CloseOfD, 104m, 2m).Value);

    /// <summary>The application clock sits on the Sunday evening before the plans (2099-01-04, 21:10 India time), so that Monday is the first session that has not opened; the database clock is the real one.</summary>
    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        Factory.Clock.Pin(new DateTimeOffset(2099, 1, 4, 15, 40, 0, TimeSpan.Zero));
    }

    private async Task PublishAsync(string key, DateOnly session, params NewPlanItem[] items)
    {
        var plan = new NewPlan(key, session, new DateTimeOffset(session.ToDateTime(new TimeOnly(3, 35)), TimeSpan.Zero), 1, new string('c', 64), "cell statistics only", true, "test", DateTimeOffset.UnixEpoch, items);
        using var scope = Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IPlanStore>().PublishAsync(plan, Ct);
    }

    private async Task SwitchAsync(bool enabled)
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "INSERT INTO paper.shadow_settings (enabled, actor, reason) VALUES (@e, 'owner:test@example.com', 'test')", ("e", enabled));
    }

    private async Task<ShadowWriteOutcome> RunAsync(DateOnly? session = null)
    {
        using var scope = Factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<WriteShadowCommand, ShadowWriteOutcome>>().HandleAsync(new WriteShadowCommand(session), Ct);
        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }

    private async Task<IReadOnlyList<ShadowChainSession>> ChainAsync()
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IShadowStore>().ReadChainAsync(Ct);
    }

    private async Task PublishNightlyPlanAsync(string key, DateOnly session, string prefix) =>
        await PublishAsync(
            key, session,
            Item(1, TicketGroup.Primary, prefix + "P1"), Item(2, TicketGroup.Primary, prefix + "P2"),
            Item(3, TicketGroup.Reserve, prefix + "R1"), Item(4, TicketGroup.Reserve, prefix + "R2"), Item(5, TicketGroup.Reserve, prefix + "R3"),
            Item(6, TicketGroup.Rejected, prefix + "X1"), Item(7, TicketGroup.Rejected, prefix + "D1", reachedFill: false));

    [Fact]
    public async Task Every_candidate_of_the_stored_plan_is_written_once_six_count_for_edge_the_duplicate_does_not_and_a_second_run_stores_nothing()
    {
        await PublishNightlyPlanAsync("shadow-1", Monday, "NSE:");
        await SwitchAsync(true);

        var first = await RunAsync();
        var second = await RunAsync();

        first.Sessions.ShouldBe(1);
        first.Rows.ShouldBe(7);
        first.EdgeRows.ShouldBe(6);
        second.Sessions.ShouldBe(0);
        second.Rows.ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM paper.shadow_entries")).ShouldBe(7);
        (await ScalarAsync<long>("SELECT count(*) FROM paper.shadow_entries WHERE counts_for_edge")).ShouldBe(6);
        (await ScalarAsync<bool>("SELECT counts_for_edge FROM paper.shadow_entries WHERE symbol = 'NSE:D1'")).ShouldBeFalse();
        (await ScalarAsync<string>("SELECT string_agg(DISTINCT ticket_group, ',' ORDER BY ticket_group) FROM paper.shadow_entries")).ShouldBe("primary,rejected,reserve");
        (await ScalarAsync<int>("SELECT n_rows FROM paper.shadow_sessions WHERE session_date = @d", ("d", Monday))).ShouldBe(7);
        (await ScalarAsync<long>("SELECT count(*) FROM paper.shadow_entries e JOIN trading.plan_items i ON i.id = e.plan_item_id AND i.plan_id = e.plan_id")).ShouldBe(7);
        (await ScalarAsync<long>("SELECT count(*) FROM paper.shadow_entries WHERE written_at >= (session_date + time '09:00') AT TIME ZONE 'Asia/Kolkata'")).ShouldBe(0);
    }

    [Fact]
    public async Task The_chain_recomputed_from_the_stored_rows_matches_and_one_changed_value_breaks_it_from_that_session_on()
    {
        await PublishNightlyPlanAsync("shadow-2a", Monday, "NSE:");
        await PublishAsync("shadow-2b", Tuesday, Item(1, TicketGroup.Primary, "NSE:T1"), Item(2, TicketGroup.Reserve, "NSE:T2"));
        await SwitchAsync(true);
        (await RunAsync()).Sessions.ShouldBe(2);

        var stored = await ChainAsync();
        stored.Select(s => s.SessionDate).ShouldBe([Monday, Tuesday]);
        stored[1].PrevDigest.ShouldBe(stored[0].Digest);
        ShadowDigest.FirstBroken(stored.ToList()).ShouldBeNull();

        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "UPDATE paper.shadow_entries SET symbol = 'NSE:CHANGED' WHERE symbol = 'NSE:P1'");
        }

        ShadowDigest.FirstBroken((await ChainAsync()).ToList()).ShouldBe(Monday);

        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "UPDATE paper.shadow_entries SET symbol = 'NSE:P1' WHERE symbol = 'NSE:CHANGED'");
            await ExecuteAsync(connection, "UPDATE paper.shadow_entries SET stop = stop + 0.0001 WHERE symbol = 'NSE:T2'");
        }

        ShadowDigest.FirstBroken((await ChainAsync()).ToList()).ShouldBe(Tuesday);
    }

    [Fact]
    public async Task A_past_session_writes_no_row_even_with_a_plan_stored_and_records_a_gap_that_is_never_filled_in()
    {
        var past = new DateOnly(2026, 10, 9); // opened before the pinned clock and before the real one
        await PublishAsync("shadow-3", past, Item(1, TicketGroup.Primary, "NSE:OLD"));
        await SwitchAsync(true);

        var outcome = await RunAsync(past);
        var again = await RunAsync(past);

        outcome.Gaps.ShouldBe(1);
        outcome.Rows.ShouldBe(0);
        again.Gaps.ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM paper.shadow_entries")).ShouldBe(0);
        (await ScalarAsync<string>("SELECT status FROM paper.shadow_sessions WHERE session_date = @d", ("d", past))).ShouldBe("gap");
        ShadowDigest.FirstBroken((await ChainAsync()).ToList()).ShouldBeNull();
    }

    [Fact]
    public async Task With_the_switch_off_by_default_or_by_a_later_row_nothing_is_written()
    {
        await PublishNightlyPlanAsync("shadow-4", Monday, "NSE:");

        var byDefault = await RunAsync();
        await SwitchAsync(true);
        await SwitchAsync(false);
        var afterOff = await RunAsync();

        byDefault.Enabled.ShouldBeFalse();
        afterOff.Enabled.ShouldBeFalse();
        (await ScalarAsync<long>("SELECT count(*) FROM paper.shadow_sessions")).ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM paper.shadow_entries")).ShouldBe(0);

        await SwitchAsync(true);
        (await RunAsync()).Rows.ShouldBe(7);
    }

    private static readonly Guid AccountId = Guid.Parse("0b6f2a9e-7a52-4d7e-9c3a-5d0a7e0b7a05");

    /// <summary>One armed, owner-approved primary paper entry with its exit in the epoch of Monday, so that the counted set and the band of P10 are not empty.</summary>
    private async Task SeedCapturedPrimaryEntryAsync()
    {
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(
                connection,
                """
                INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget)
                VALUES (@id, 'probe-shadow', 'Probe', 'Dhan', '1000000081', current_date, '#1A2B3C', now(), now(), false, false, false, 200)
                ON CONFLICT (id) DO NOTHING
                """,
                ("id", AccountId));
        }

        var cutoff = new DateTimeOffset(2099, 1, 5, 3, 35, 0, TimeSpan.Zero);
        var afterClose = new DateTimeOffset(2099, 1, 5, 11, 30, 0, TimeSpan.Zero);
        Factory.Clock.Pin(new DateTimeOffset(2099, 1, 5, 2, 0, 0, TimeSpan.Zero));
        var item = new NewPlanItem(
            1, TicketGroup.Primary, "NSE:PAPA", "BUY", "ORB", new string('a', 64), "close above 15m high", "0.8 ATR below entry", "2R", "09:30-11:30", [],
            new PlanEvidence(Cell, 41, "replayed history", "cell statistics only", 0.72, 0.12, [], null, 0.7), [new NewAllocation(AccountId, "paper", 100)]);
        var plan = new NewPlan("shadow-golden", Monday, cutoff, 1, new string('c', 64), "cell statistics only", true, "test", DateTimeOffset.UnixEpoch, [item]);
        using var scope = Factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var plans = services.GetRequiredService<IPlanStore>();
        var published = await plans.PublishAsync(plan, Ct);
        var id = (await plans.ItemsAsync(published.Id, Ct)).Single().Id;
        (await services.GetRequiredService<ICommandHandler<MoveTicketCommand, TicketMoveOutcome>>().HandleAsync(new MoveTicketCommand(id, TicketState.Approved, "owner:test@example.com", "approved for watching"), Ct)).IsSuccess.ShouldBeTrue();
        (await services.GetRequiredService<ICommandHandler<MovePlanCommand, PlanMoveOutcome>>().HandleAsync(new MovePlanCommand(published.Id, PlanState.Approved, "system:test", "approved"), Ct)).IsSuccess.ShouldBeTrue();
        Factory.Clock.Pin(afterClose.AddHours(-7));
        (await plans.MoveItemAsync(new ItemMove(id, TicketState.Approved, TicketState.Armed, "system:preopen", "armed", null), Ct)).ShouldBe(MoveStatus.Created);
        Factory.Clock.Pin(afterClose);
        (await plans.MoveItemAsync(new ItemMove(id, TicketState.Armed, TicketState.Expired, "system:plan-expiry", "expired", null), Ct)).ShouldBe(MoveStatus.Created);

        var paper = services.GetRequiredService<IPaperStore>();
        var epoch = await paper.EnsureFirstEpochAsync(Monday, afterClose, Ct);
        var approval = await ScalarAsync<long>("SELECT id FROM workflow.approvals WHERE item_id = @i", ("i", id));
        var armed = new DateTimeOffset(DateTime.SpecifyKind(await ScalarAsync<DateTime>("SELECT created_at FROM trading.plan_events WHERE item_id = @i AND to_state = 'armed'", ("i", id)), DateTimeKind.Utc));
        var entry = new PaperEntry(published.Id, id, AccountId, "NSE:PAPA", "BUY", "ORB", new string('a', 64), Cell, "primary", approval, "owner", 100, 60, armed, armed.AddMinutes(10), 100.15m, 100.10m, 99m, 102.3m, false, "ORB|NSE:PAPA|Buy|2099-01-05", new string('d', 64), "sim-6");
        var exit = new PaperExit(armed.AddMinutes(70), 102.30m, "Target", 1, 129m, null, null, null, PaperWording.Gross, 2, 1.95, null, 1.95, "gross", 0.4, 2.1);
        await paper.WriteSessionAsync(new SessionWrite(epoch.Id, [new EpochAccount(AccountId, 100_000m, "test")], [new NewPaperEntry(entry, "measured", exit)], [], [], new NewCaptureRun(Monday, afterClose, afterClose, "done", 1, 0, 0, true, "test")), Ct);
    }

    [Fact]
    public async Task The_counted_set_and_the_band_of_P10_are_byte_identical_with_and_without_shadow_rows_when_a_captured_primary_entry_exists()
    {
        await SeedCapturedPrimaryEntryAsync(); // the clock is left at the close of Monday, so Tuesday has not opened
        await PublishNightlyPlanAsync("shadow-5", Tuesday, "NSE:");
        await ResignInAsync();
        string[] reads = ["/api/paper/phase-check", "/api/paper/backtest-vs-paper", "/api/paper/entries?limit=50", "/api/paper/capture-runs?limit=50", "/api/paper/epochs"];

        async Task<string[]> ReadAsync() => [.. await Task.WhenAll(reads.Select(path => Client.GetStringAsync(new Uri(path, UriKind.Relative), Ct)))];

        var without = await ReadAsync();
        await SwitchAsync(true);
        (await RunAsync()).Rows.ShouldBe(7);
        var with = await ReadAsync();

        with.ShouldBe(without);
        using var compare = System.Text.Json.JsonDocument.Parse(with[1]);
        compare.RootElement.GetProperty("counts").GetProperty("counted").GetInt32().ShouldBe(1); // the golden output is not empty: one counted trade
        compare.RootElement.GetProperty("n").GetInt32().ShouldBe(1);
        var summary = await Client.GetFromJsonAsync<System.Text.Json.JsonElement>(new Uri("/api/paper/shadow/summary", UriKind.Relative), Ct);
        summary.GetProperty("counts").GetProperty("behavioural").GetInt32().ShouldBe(1); // the seven shadow candidates are not added to the one paper trade
        summary.GetProperty("counts").GetProperty("candidates").GetInt32().ShouldBe(7);
        summary.GetProperty("counts").GetProperty("edgeRaw").GetInt32().ShouldBe(0);
    }
}
