using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Live;
using XDrishti.Application.Planning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Planning;

namespace XDrishti.Api.IntegrationTests.Planning;

/// <summary>
/// Spec P7-11 (ADR 0096) on a real database: an approved ticket is armed by the service move that the existing table and triggers allow, the DATABASE refuses arming a ticket that is not approved, the armed instrument is read by the
/// feed's provider and leaves at expiry, an invalidated ticket leaves an inbox row, and a second run changes nothing.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class PreOpenArmingDatabaseTests(ApiFactory factory) : DataTestBase(factory)
{
    private const string OwnerActor = "owner:test@example.com";

    private static readonly Guid AccountId = Guid.Parse("0b6f2a9e-7a52-4d7e-9c3a-5d0a7e0b7a03");

    private static readonly DateOnly Session = new(2099, 1, 5);

    /// <summary>The review cutoff, 09:05 India time.</summary>
    private static readonly DateTimeOffset Cutoff = new(2099, 1, 5, 3, 35, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset BeforeCutoff = new(2099, 1, 5, 2, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset PreOpen = new(2099, 1, 5, 3, 40, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset AfterClose = new(2099, 1, 5, 10, 10, 0, TimeSpan.Zero);

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        await ExecuteAsync(
            connection,
            """
            INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget)
            VALUES (@id, 'probe-preopen', 'Probe', 'Dhan', '1000000079', current_date, '#1A2B3C', now(), now(), false, false, false, 200)
            """,
            ("id", AccountId));
    }

    private static NewPlanItem Item(int rank, string symbol) =>
        new(
            rank, TicketGroup.Primary, symbol, "BUY", "ORB", new string('a', 64), "close above 15m high", "0.8 ATR below entry", "2R", "09:30-11:30", [],
            new PlanEvidence("ORB|15m|BUY|intraday|FIXED_RR|*", 41, "about 2.1 years, replayed history, modelled fills, no hold-out", "shrunk posterior win rate of the cell", 0.72, 0.12, [], null, 0.7),
            [new NewAllocation(AccountId, "paper", 10)]);

    private async Task<TResult> WithAsync<TResult>(Func<IServiceProvider, Task<TResult>> work)
    {
        using var scope = Factory.Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    /// <summary>Publishes a plan for the session, approves each ticket as the owner before the cutoff and moves the plan to Approved.</summary>
    private async Task<(long Plan, long[] Items)> ApprovedPlanAsync(params string[] symbols)
    {
        Factory.Clock.Pin(BeforeCutoff);
        var plan = new NewPlan("preopen-" + string.Join('-', symbols), Session, Cutoff, 1, new string('c', 64), "cell statistics only", true, "test", DateTimeOffset.UnixEpoch, [.. symbols.Select((s, i) => Item(i + 1, s))]);
        var published = await WithAsync(p => p.GetRequiredService<IPlanStore>().PublishAsync(plan, Ct).AsTask());
        var items = (await WithAsync(p => p.GetRequiredService<IPlanStore>().ItemsAsync(published.Id, Ct).AsTask())).Select(i => i.Id).ToArray();
        foreach (var item in items)
        {
            var approved = await WithAsync(p => p.GetRequiredService<ICommandHandler<MoveTicketCommand, TicketMoveOutcome>>().HandleAsync(new MoveTicketCommand(item, TicketState.Approved, OwnerActor, "approved for watching"), Ct).AsTask());
            approved.IsSuccess.ShouldBeTrue();
        }

        var moved = await WithAsync(p => p.GetRequiredService<ICommandHandler<MovePlanCommand, PlanMoveOutcome>>().HandleAsync(new MovePlanCommand(published.Id, PlanState.Approved, "system:test", "approved"), Ct).AsTask());
        moved.IsSuccess.ShouldBeTrue();
        Factory.Clock.Pin(PreOpen);
        return (published.Id, items);
    }

    private Task<Result<PreOpenOutcome>> RunPreOpenAsync(IPreOpenSource source) =>
        WithAsync(p => new RunPreOpenCheckHandler(
            p.GetRequiredService<IPlanStore>(),
            source,
            p.GetRequiredService<ICommandHandler<MoveTicketCommand, TicketMoveOutcome>>(),
            p.GetRequiredService<ICommandHandler<MovePlanCommand, PlanMoveOutcome>>(),
            p.GetRequiredService<ISuggestionStore>(),
            p.GetRequiredService<ILiveNotifier>(),
            Factory.Clock,
            NullLogger<RunPreOpenCheckHandler>.Instance).HandleAsync(new RunPreOpenCheckCommand(), Ct).AsTask());

    private async Task<string> StateAsync(long item) =>
        await ScalarAsync<string>("SELECT to_state FROM trading.plan_events WHERE item_id = @i ORDER BY id DESC LIMIT 1", ("i", item));

    private async Task<string> PlanStateAsync(long plan) =>
        await ScalarAsync<string>("SELECT to_state FROM trading.plan_events WHERE plan_id = @p AND item_id IS NULL ORDER BY id DESC LIMIT 1", ("p", plan));

    private static FakeSource Source(params (string Symbol, double? Last, double? Previous, double? Atr)[] readings) =>
        new(readings.ToDictionary(r => r.Symbol, r => new PreOpenReading(r.Last, r.Previous, r.Atr), StringComparer.Ordinal));

    private sealed class FakeSource(Dictionary<string, PreOpenReading> readings) : IPreOpenSource
    {
        public ValueTask<PreOpenSnapshot> ReadAsync(IReadOnlyCollection<string> symbols, DateOnly today, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new PreOpenSnapshot(readings, null));
    }

    [Fact]
    public async Task An_approved_ticket_is_armed_enters_the_feed_set_and_leaves_at_expiry()
    {
        var instrument = await SeedAsync("ARMA");
        var (plan, items) = await ApprovedPlanAsync("NSE:ARMA");
        var securityId = await ScalarAsync<string>("SELECT broker_id FROM market.instrument_broker_ids WHERE instrument_id = @i", ("i", instrument));

        var result = await RunPreOpenAsync(Source(("NSE:ARMA", 101.0, 100.0, 4.0)));

        result.Value.Armed.ShouldBe(1);
        (await StateAsync(items[0])).ShouldBe("armed");
        (await PlanStateAsync(plan)).ShouldBe("armed");
        (await ScalarAsync<string>("SELECT actor FROM trading.plan_events WHERE item_id = @i AND to_state = 'armed'", ("i", items[0]))).ShouldBe("system:preopen");
        (await ScalarAsync<string>("SELECT reason FROM trading.plan_events WHERE item_id = @i AND to_state = 'armed'", ("i", items[0]))).ShouldStartWith(PlanWording.Armed);

        var inSet = await WithAsync(async p => await new ArmedTicketsProvider(p.GetRequiredService<IArmedTicketReader>(), p.GetRequiredService<IInstrumentSecurities>(), Factory.Clock, NullLogger<ArmedTicketsProvider>.Instance).ListAsync(Ct));
        var candidate = inSet.ShouldHaveSingleItem();
        candidate.InstrumentId.ShouldBe(instrument);
        candidate.SecurityId.ShouldBe(securityId);
        candidate.Reason.ShouldBe(ActiveSetReason.ArmedTicket);

        Factory.Clock.Pin(AfterClose);
        var expiry = await WithAsync(p => p.GetRequiredService<ICommandHandler<ExpireTicketsCommand, ExpiryOutcome>>().HandleAsync(new ExpireTicketsCommand(), Ct).AsTask());
        expiry.Value.Tickets.ShouldBe(1);
        (await StateAsync(items[0])).ShouldBe("expired");
        (await PlanStateAsync(plan)).ShouldBe("expired");
        var after = await WithAsync(async p => await new ArmedTicketsProvider(p.GetRequiredService<IArmedTicketReader>(), p.GetRequiredService<IInstrumentSecurities>(), Factory.Clock, NullLogger<ArmedTicketsProvider>.Instance).ListAsync(Ct));
        after.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_gap_invalidates_in_the_database_leaves_an_inbox_row_and_a_second_run_changes_nothing()
    {
        await SeedAsync("GAPA");
        await SeedAsync("OKAY");
        var (plan, items) = await ApprovedPlanAsync("NSE:GAPA", "NSE:OKAY");
        var source = Source(("NSE:GAPA", 108.0, 100.0, 4.0), ("NSE:OKAY", 100.5, 100.0, 4.0));

        var first = await RunPreOpenAsync(source);
        var events = await ScalarAsync<long>("SELECT count(*) FROM trading.plan_events");
        var second = await RunPreOpenAsync(source);

        first.Value.Armed.ShouldBe(1);
        first.Value.Invalidated.ShouldBe(1);
        (await StateAsync(items[0])).ShouldBe("invalidated");
        (await StateAsync(items[1])).ShouldBe("armed");
        (await PlanStateAsync(plan)).ShouldBe("armed");
        (await ScalarAsync<string>("SELECT text FROM workflow.suggestions WHERE plan_id = @p AND kind = 'ticket_invalidated_preopen'", ("p", plan))).ShouldContain("gap_too_large");
        second.Value.Armed.ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM trading.plan_events")).ShouldBe(events);
        (await ScalarAsync<long>("SELECT count(*) FROM workflow.suggestions WHERE kind = 'ticket_invalidated_preopen'")).ShouldBe(1);
    }

    [Fact]
    public async Task A_ticket_whose_price_is_unavailable_is_invalidated_and_the_plan_expires_with_nothing_armed()
    {
        await SeedAsync("NOPE");
        var (plan, items) = await ApprovedPlanAsync("NSE:NOPE");

        var result = await RunPreOpenAsync(Source());

        result.Value.Armed.ShouldBe(0);
        (await StateAsync(items[0])).ShouldBe("invalidated");
        (await PlanStateAsync(plan)).ShouldBe("expired");
        (await ScalarAsync<long>("SELECT count(*) FROM trading.plan_events WHERE to_state = 'armed'")).ShouldBe(0);
    }

    [Theory]
    [InlineData("proposed")]
    [InlineData("lapsed")]
    [InlineData("rejected")]
    [InlineData("modified")]
    public async Task The_database_refuses_to_arm_a_ticket_that_is_not_approved(string from)
    {
        Factory.Clock.Pin(BeforeCutoff);
        var plan = new NewPlan("notapproved-" + from, Session, Cutoff, 1, new string('c', 64), "cell statistics only", true, "test", DateTimeOffset.UnixEpoch, [Item(1, "NSE:ZZZ")]);
        var published = await WithAsync(p => p.GetRequiredService<IPlanStore>().PublishAsync(plan, Ct).AsTask());
        var item = (await WithAsync(p => p.GetRequiredService<IPlanStore>().ItemsAsync(published.Id, Ct).AsTask())).Single().Id;

        if (from != "proposed")
        {
            var to = from switch { "lapsed" => TicketState.Lapsed, "rejected" => TicketState.Rejected, _ => TicketState.Modified };
            var actor = to == TicketState.Lapsed ? "system:test" : OwnerActor;
            var moved = await WithAsync(p => p.GetRequiredService<ICommandHandler<MoveTicketCommand, TicketMoveOutcome>>().HandleAsync(new MoveTicketCommand(item, to, actor, "r", to == TicketState.Modified ? "qty:x=1" : null), Ct).AsTask());
            moved.IsSuccess.ShouldBeTrue();
        }

        // A direct insert of an armed row that continues the ticket's trail; the check on the legal pairs refuses it (23514) whatever the actor.
        await using var connection = await OpenAsync();
        await using var insert = new NpgsqlCommand(
            "INSERT INTO trading.plan_events (plan_id, item_id, from_state, to_state, actor, reason, prev_event_id, created_at) VALUES (@p, @i, @from, 'armed', 'system:preopen', 'r', (SELECT max(id) FROM trading.plan_events WHERE item_id = @i), now())",
            connection);
        insert.Parameters.AddWithValue("p", published.Id);
        insert.Parameters.AddWithValue("i", item);
        insert.Parameters.AddWithValue("from", from);

        var failure = await Should.ThrowAsync<PostgresException>(async () => await insert.ExecuteNonQueryAsync(Ct));

        failure.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        (await ScalarAsync<long>("SELECT count(*) FROM trading.plan_events WHERE to_state = 'armed'")).ShouldBe(0);
    }

    [Fact]
    public async Task With_nothing_armed_the_feed_provider_proposes_nothing()
    {
        await SeedAsync("IDLE");
        await ApprovedPlanAsync("NSE:IDLE");

        // Approved but never checked (the service is paused): not in the set.
        var inSet = await WithAsync(async p => await new ArmedTicketsProvider(p.GetRequiredService<IArmedTicketReader>(), p.GetRequiredService<IInstrumentSecurities>(), Factory.Clock, NullLogger<ArmedTicketsProvider>.Instance).ListAsync(Ct));

        inSet.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_instrument_lookup_leaves_out_retired_unmapped_and_ambiguous_symbols()
    {
        var ok = await SeedAsync("LOOKOK");
        await SeedAsync("RETIRED", retired: true);
        await SeedAsync("NODHAN", dhanId: false);

        var found = await WithAsync(p => p.GetRequiredService<IInstrumentSecurities>().FindAsync(["NSE:LOOKOK", "NSE:RETIRED", "NSE:NODHAN", "NSE:MISSING"], Ct).AsTask());

        found.Keys.ShouldBe(["NSE:LOOKOK"]);
        found["NSE:LOOKOK"].InstrumentId.ShouldBe(ok);
    }
}
