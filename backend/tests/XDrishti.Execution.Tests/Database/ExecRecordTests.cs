using Npgsql;
using XDrishti.Execution.Application;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Persistence;

namespace XDrishti.Execution.Tests.Database;

/// <summary>Criteria 7 and 8 of P12-01 in their database form (P12-01b): the event log, its constraints and triggers, and the projection as a fold. Runs on a dedicated server, as the role xd_exec.</summary>
[Collection(ExecDatabaseSuite.Name)]
public sealed class ExecRecordTests(ExecDatabase db)
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 29, 4, 0, 0, TimeSpan.Zero);
    private static long _next = 1000;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private NpgsqlDataSource Data() => NpgsqlDataSource.Create(db.Exec);

    private ExecIntent NewIntent(int qty = 10) =>
        ExecIntent.Create(Interlocked.Increment(ref _next), Guid.NewGuid(), db.AccountId, _next, ExecSide.Buy, "TCS", "11536", qty, 100m, 98m, 104m, ExecStructure.Super).Value;

    private static ExecEvent Move(int seq, ExecState from, ExecState to) => ExecEvent.Move(seq, from, to, ExecEventSource.Engine, "engine", T0.AddSeconds(seq));

    private static readonly (ExecState From, ExecState To)[] Walk =
    [
        (ExecState.Planned, ExecState.Armed), (ExecState.Armed, ExecState.Checking), (ExecState.Checking, ExecState.Submitting), (ExecState.Submitting, ExecState.Open),
    ];

    private async Task<(PgExecEventStore Store, ExecIntent Intent)> StartedAsync(int qty = 10)
    {
        var store = new PgExecEventStore(Data());
        var intent = NewIntent(qty);
        (await store.StartAsync(intent, new DateOnly(2026, 10, 29), ExecEnvironment.Sandbox, "system:test", Ct)).IsSuccess.ShouldBeTrue();
        return (store, intent);
    }

    [Fact]
    public async Task The_database_lists_the_same_23_moves_as_the_domain()
    {
        var states = Enum.GetValues<ExecState>();
        var legal = 0;
        foreach (var from in states)
        {
            foreach (var to in states)
            {
                var inDatabase = (bool)(await ExecDatabase.ScalarAsync(db.Exec, $"SELECT execution.legal_move('{from.ToString().ToLowerInvariant()}', '{to.ToString().ToLowerInvariant()}')"))!;
                inDatabase.ShouldBe(ExecLifecycle.IsLegal(from, to), $"{from} to {to}");
                legal += inDatabase ? 1 : 0;
            }
        }

        legal.ShouldBe(23);
        (states.Length * states.Length).ShouldBe(196);
    }

    [Fact]
    public async Task An_intent_walks_its_chain_and_the_projection_follows_in_the_same_transaction()
    {
        var (store, intent) = await StartedAsync();
        (await store.StateAsync(intent.Id, Ct)).ShouldBe(new ExecIntentState(ExecState.Planned, 0, 0));
        var seq = 0;
        foreach (var (from, to) in Walk)
        {
            (await store.AppendAsync(intent.Id, Move(++seq, from, to), Ct)).IsSuccess.ShouldBeTrue();
        }

        (await store.AppendAsync(intent.Id, ExecEvent.Fill(++seq, 4, ExecEventSource.BrokerUpdate, "broker", T0.AddMinutes(1)), Ct)).IsSuccess.ShouldBeTrue();
        (await store.StateAsync(intent.Id, Ct)).ShouldBe(new ExecIntentState(ExecState.Open, 5, 4));
        (await store.EventsAsync(intent.Id, Ct)).Count.ShouldBe(5);
        var rebuilt = (await store.RebuildAsync(intent.Id, Ct)).Value;
        (rebuilt.State, rebuilt.Seq, rebuilt.FilledQty).ShouldBe((ExecState.Open, 5, 4));
    }

    [Fact]
    public async Task A_refused_move_stores_nothing_and_a_part_filled_entry_is_not_cancelled()
    {
        var (store, intent) = await StartedAsync();
        (await store.AppendAsync(intent.Id, Move(1, ExecState.Planned, ExecState.Open), Ct)).Error.Code.ShouldBe(ExecLifecycle.IllegalMoveCode);
        (await store.AppendAsync(intent.Id, Move(2, ExecState.Planned, ExecState.Armed), Ct)).Error.Code.ShouldBe("execution.event_conflict");
        (await store.EventsAsync(intent.Id, Ct)).ShouldBeEmpty();
        (await store.StateAsync(intent.Id, Ct)).ShouldBe(new ExecIntentState(ExecState.Planned, 0, 0));

        var seq = 0;
        foreach (var (from, to) in Walk)
        {
            await store.AppendAsync(intent.Id, Move(++seq, from, to), Ct);
        }

        await store.AppendAsync(intent.Id, ExecEvent.Fill(++seq, 3, ExecEventSource.BrokerUpdate, "broker", T0), Ct);
        (await store.AppendAsync(intent.Id, Move(++seq, ExecState.Open, ExecState.Cancelled), Ct)).IsFailure.ShouldBeTrue();
        (await store.AppendAsync(intent.Id, ExecEvent.Fill(seq, 8, ExecEventSource.BrokerUpdate, "broker", T0), Ct)).IsFailure.ShouldBeTrue("a fill above the quantity");
        (await store.StateAsync(intent.Id, Ct))!.Seq.ShouldBe(5);
    }

    [Fact]
    public async Task A_second_intent_for_the_same_ticket_and_an_unknown_intent_are_refused()
    {
        var (store, intent) = await StartedAsync();
        var again = ExecIntent.Create(intent.Id + 100000, Guid.NewGuid(), intent.AccountId, intent.PlanItemId, ExecSide.Buy, "TCS", "11536", 5, 100m, 98m, 104m, ExecStructure.Super).Value;

        (await store.StartAsync(again, new DateOnly(2026, 10, 29), ExecEnvironment.Sandbox, "system:test", Ct)).Error.Code.ShouldBe("execution.event_conflict");
        (await store.StartAsync(NewIntent(), new DateOnly(2026, 10, 29), ExecEnvironment.None, "system:test", Ct)).Error.Code.ShouldBe("execution.gate_closed");
        (await store.AppendAsync(999999, Move(1, ExecState.Planned, ExecState.Armed), Ct)).Error.Code.ShouldBe("execution.intent_unknown");
        (await store.StateAsync(999999, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task The_database_itself_refuses_an_illegal_pair_a_fork_a_gap_a_wrong_start_and_a_late_cancel()
    {
        var (store, intent) = await StartedAsync();
        var id = intent.Id;
        string Insert(int seq, string kind, string? from, string? to, string? prev, string payload = "{}") =>
            $"INSERT INTO execution.events (intent_id, seq, kind, from_state, to_state, prev_event_id, source, actor, payload) VALUES ({id}, {seq}, '{kind}', {Q(from)}, {Q(to)}, {prev ?? "NULL"}, 'engine', 'x', '{payload}')";

        (await ExecDatabase.SqlStateAsync(db.Exec, Insert(1, "state", "planned", "open", null))).ShouldBe("23514"); // illegal pair
        (await ExecDatabase.SqlStateAsync(db.Exec, Insert(1, "state", "armed", "checking", null))).ShouldBe("23514"); // does not start from planned
        (await ExecDatabase.SqlStateAsync(db.Exec, Insert(2, "state", "planned", "armed", null))).ShouldBe("23514"); // a gap
        (await ExecDatabase.SqlStateAsync(db.Exec, Insert(1, "state", "planned", "armed", null))).ShouldBeNull();
        (await ExecDatabase.SqlStateAsync(db.Exec, Insert(1, "state", "planned", "armed", null))).ShouldNotBeNull(); // a second start
        var first = (long)(await ExecDatabase.ScalarAsync(db.Exec, $"SELECT id FROM execution.events WHERE intent_id = {id}"))!;
        (await ExecDatabase.SqlStateAsync(db.Exec, Insert(2, "state", "armed", "checking", null))).ShouldBe("23514"); // seq 2 must point at event 1
        (await ExecDatabase.SqlStateAsync(db.Exec, Insert(2, "state", "armed", "checking", first.ToString(System.Globalization.CultureInfo.InvariantCulture)))).ShouldBeNull();
        (await ExecDatabase.SqlStateAsync(db.Exec, Insert(3, "alarm", "armed", null, null))).ShouldNotBeNull(); // a non-state event carries no states
        (await ExecDatabase.SqlStateAsync(db.Exec, Insert(3, "fill", null, null, "(SELECT max(id) FROM execution.events WHERE intent_id = " + id + ")", """{"fill_qty":1}"""))).ShouldBeNull();
        (await ExecDatabase.SqlStateAsync(db.Exec, Insert(4, "alarm", null, null, "(SELECT max(id) FROM execution.events WHERE intent_id = " + id + ")", """{"note":"my token is x"}"""))).ShouldNotBeNull(); // a secret word
        (await store.StateAsync(id, Ct)).ShouldBe(new ExecIntentState(ExecState.Checking, 3, 1));
    }

    private static string Q(string? text) => text is null ? "NULL" : $"'{text}'";

    [Fact]
    public async Task Dropping_and_rebuilding_the_projection_of_200_random_intents_gives_the_live_one()
    {
        var store = new PgExecEventStore(Data());
        var random = new Random(12);
        var states = Enum.GetValues<ExecState>();
        for (var n = 0; n < 200; n++)
        {
            var intent = NewIntent(20);
            await store.StartAsync(intent, new DateOnly(2026, 10, 29), ExecEnvironment.Sandbox, "system:test", Ct);
            var state = ExecState.Planned;
            var seq = 0;
            for (var step = 0; step < random.Next(1, 14); step++)
            {
                var to = states[random.Next(states.Length)];
                var e = random.Next(4) == 0 && state is ExecState.Open or ExecState.Filled or ExecState.Protected
                    ? ExecEvent.Fill(seq + 1, random.Next(1, 6), ExecEventSource.BrokerUpdate, "broker", T0)
                    : Move(seq + 1, state, to);
                if ((await store.AppendAsync(intent.Id, e, Ct)).IsSuccess)
                {
                    seq++;
                    state = e.To ?? state;
                }
            }

            var live = (await store.StateAsync(intent.Id, Ct))!;
            var rebuilt = (await store.RebuildAsync(intent.Id, Ct)).Value;
            live.ShouldBe(new ExecIntentState(rebuilt.State, rebuilt.Seq, rebuilt.FilledQty));
        }

        // The same fold, done by the database over every intent at once: nothing drifted.
        var drift = await ExecDatabase.ScalarAsync(db.Owner, """
            SELECT count(*) FROM execution.intent_state s WHERE (s.state, s.seq, s.filled_qty) IS DISTINCT FROM (
                COALESCE((SELECT e.to_state FROM execution.events e WHERE e.intent_id = s.intent_id AND e.kind = 'state' ORDER BY e.seq DESC LIMIT 1), 'planned'),
                COALESCE((SELECT max(e.seq) FROM execution.events e WHERE e.intent_id = s.intent_id), 0),
                COALESCE((SELECT sum((e.payload ->> 'fill_qty')::int) FROM execution.events e WHERE e.intent_id = s.intent_id AND e.kind = 'fill'), 0))
            """);
        drift.ShouldBe(0L);
    }

    [Fact]
    public async Task Two_writers_of_one_intent_never_fork_it()
    {
        var (store, intent) = await StartedAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() => store.AppendAsync(intent.Id, Move(1, ExecState.Planned, ExecState.Armed), Ct), Ct)));

        results.Count(r => r.IsSuccess).ShouldBe(1);
        (await store.EventsAsync(intent.Id, Ct)).Count.ShouldBe(1);
    }
}
