using XDrishti.Domain.Common;
using XDrishti.Execution.Application;
using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Tests;

public sealed class ExecIntentTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 29, 4, 0, 0, TimeSpan.Zero);
    private static readonly ExecState[] All = Enum.GetValues<ExecState>();

    internal static ExecIntent Make(int qty = 10) =>
        ExecIntent.Create(1, Guid.NewGuid(), Guid.NewGuid(), 7, ExecSide.Buy, "TCS", "11536", qty, 100m, 98m, 104m, ExecStructure.Super).Value;

    private static ExecEvent Move(ExecIntent i, ExecState to) => ExecEvent.Move(i.Seq + 1, i.State, to, ExecEventSource.Engine, "test", At);

    private static ExecIntent Go(ExecIntent i, params ExecState[] path) => path.Aggregate(i, (cur, to) => cur.Apply(Move(cur, to)).Value);

    [Fact]
    public void A_legal_path_is_applied_and_the_intent_is_never_changed_in_place()
    {
        var start = Make();
        var armed = start.Apply(Move(start, ExecState.Armed));

        armed.IsSuccess.ShouldBeTrue();
        armed.Value.State.ShouldBe(ExecState.Armed);
        start.State.ShouldBe(ExecState.Planned);
        start.Seq.ShouldBe(0);
    }

    [Fact]
    public void Open_to_cancelled_is_refused_once_a_fill_exists()
    {
        var open = Go(Make(), ExecState.Armed, ExecState.Checking, ExecState.Submitting, ExecState.Open);
        var filled = open.Apply(ExecEvent.Fill(open.Seq + 1, 3, ExecEventSource.BrokerUpdate, "b", At)).Value;

        filled.Apply(Move(filled, ExecState.Cancelled)).IsFailure.ShouldBeTrue();
        filled.Apply(Move(filled, ExecState.Filled)).IsSuccess.ShouldBeTrue();
        open.Apply(Move(open, ExecState.Cancelled)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_fill_never_exceeds_the_quantity_and_events_never_skip_or_name_a_secret()
    {
        var i = Go(Make(5), ExecState.Armed, ExecState.Checking, ExecState.Submitting, ExecState.Open);
        var n = i.Seq;

        i.Apply(ExecEvent.Fill(n + 1, 6, ExecEventSource.BrokerPoll, "b", At)).IsFailure.ShouldBeTrue();
        i.Apply(ExecEvent.Fill(n + 2, 1, ExecEventSource.BrokerPoll, "b", At)).Error.Code.ShouldBe("execution.event_conflict");
        i.Apply(new ExecEvent(n + 1, ExecEventKind.Alarm, null, null, ExecEventSource.Engine, "e", At, Payload: "{\"access_token\":\"x\"}")).IsFailure.ShouldBeTrue();
        i.Apply(new ExecEvent(n + 1, ExecEventKind.Alarm, null, null, ExecEventSource.Engine, "e", At, Payload: new string('a', 5000))).IsFailure.ShouldBeTrue();
        i.Apply(new ExecEvent(n + 1, ExecEventKind.Alarm, null, null, ExecEventSource.Engine, "e", At, Payload: "{\"k\":1}")).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_fill_is_accepted_only_in_the_states_where_one_can_occur()
    {
        var okStates = new[] { ExecState.Open, ExecState.Filled, ExecState.Protected, ExecState.Managing, ExecState.Closing };
        foreach (var state in All)
        {
            var i = Make(10);
            var path = PathTo(state);
            foreach (var to in path)
            {
                i = i.Apply(Move(i, to)).Value;
            }

            var r = i.Apply(ExecEvent.Fill(i.Seq + 1, 1, ExecEventSource.BrokerUpdate, "b", At));
            r.IsSuccess.ShouldBe(okStates.Contains(state), state.ToString());
        }
    }

    private static List<ExecState> PathTo(ExecState target)
    {
        var queue = new Queue<List<ExecState>>();
        queue.Enqueue([ExecState.Planned]);
        while (queue.Count > 0)
        {
            var path = queue.Dequeue();
            if (path[^1] == target)
            {
                return path[1..];
            }

            foreach (var next in ExecLifecycle.LegalFrom(path[^1]).Where(n => !path.Contains(n)))
            {
                queue.Enqueue([.. path, next]);
            }
        }

        return [];
    }

    [Fact]
    public void A_state_event_must_start_from_the_current_state()
    {
        var i = Make();

        i.Apply(ExecEvent.Move(1, ExecState.Armed, ExecState.Checking, ExecEventSource.Engine, "e", At)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void An_intent_needs_positive_values()
    {
        ExecIntent.Create(1, Guid.NewGuid(), Guid.NewGuid(), 7, ExecSide.Buy, "TCS", "1", 0, 100m, 98m, 104m, ExecStructure.Super).IsFailure.ShouldBeTrue();
        ExecIntent.Create(1, Guid.NewGuid(), Guid.NewGuid(), 7, ExecSide.Buy, " ", "1", 1, 100m, 98m, 104m, ExecStructure.Super).IsFailure.ShouldBeTrue();
    }

    /// <summary>One random walk of 1 to 40 attempted moves (and the odd fill). True when every accepted move was legal by the oracle, every refused one was not (or was a cancel after a fill), and a refusal changed nothing.</summary>
    internal static bool Walk(int seed, Func<ExecState, ExecState, bool> legal, out List<ExecEvent> accepted, out ExecIntent final)
    {
        var rnd = new Random(seed);
        var intent = Make(20);
        accepted = [];
        for (var n = rnd.Next(1, 41); n > 0; n--)
        {
            var before = (intent.State, intent.Seq, intent.FilledQty);
            ExecEvent e;
            if (rnd.Next(5) == 0)
            {
                e = ExecEvent.Fill(intent.Seq + 1, rnd.Next(1, 8), ExecEventSource.BrokerPoll, "b", At);
            }
            else
            {
                var next = ExecLifecycle.LegalFrom(intent.State);
                var to = next.Count > 0 && rnd.Next(2) == 0 ? next[rnd.Next(next.Count)] : All[rnd.Next(All.Length)];
                e = ExecEvent.Move(intent.Seq + 1, intent.State, to, ExecEventSource.Engine, "e", At);
                var expectLegal = legal(intent.State, to) && !(intent.State == ExecState.Open && to == ExecState.Cancelled && intent.FilledQty > 0);
                var r = intent.Apply(e);
                if (r.IsSuccess != expectLegal)
                {
                    final = intent;
                    return false;
                }

                if (r.IsSuccess)
                {
                    intent = r.Value;
                    accepted.Add(e);
                }
                else if ((intent.State, intent.Seq, intent.FilledQty) != before)
                {
                    final = intent;
                    return false;
                }

                continue;
            }

            var f = intent.Apply(e);
            if (f.IsSuccess)
            {
                intent = f.Value;
                accepted.Add(e);
            }
        }

        final = intent;
        return true;
    }

    [Fact]
    public void Property_5000_seeds_no_illegal_path_and_accepted_moves_replay_to_the_same_state()
    {
        for (var seed = 0; seed < 5000; seed++)
        {
            Walk(seed, ExecLifecycle.IsLegal, out var accepted, out var final).ShouldBeTrue($"seed {seed}");
            var replay = accepted.Aggregate(Make(20), (cur, e) => cur.Apply(e).Value);
            (replay.State, replay.Seq, replay.FilledQty).ShouldBe((final.State, final.Seq, final.FilledQty), $"seed {seed}");
        }
    }

    [Fact]
    public void Negative_control_a_doctored_table_allowing_closed_to_open_is_caught_on_at_least_10_seeds()
    {
        static bool Doctored(ExecState a, ExecState b) => ExecLifecycle.IsLegal(a, b) || (a == ExecState.Closed && b == ExecState.Open);

        var caught = Enumerable.Range(0, 50000).Count(seed => !Walk(seed, Doctored, out _, out _));

        caught.ShouldBeGreaterThanOrEqualTo(10);
    }

    [Fact]
    public void The_projection_is_the_fold_of_the_events_for_200_random_intents()
    {
        for (var seed = 0; seed < 200; seed++)
        {
            var store = new InMemoryExecEventStore();
            var seedIntent = Make(20);
            store.Start(seedIntent).IsSuccess.ShouldBeTrue();
            store.Start(seedIntent).IsFailure.ShouldBeTrue();
            Walk(seed, ExecLifecycle.IsLegal, out var accepted, out _);
            accepted.ForEach(e => store.Append(seedIntent.Id, e).IsSuccess.ShouldBeTrue());
            var live = store.Live(seedIntent.Id)!;
            var rebuilt = store.Rebuild(seedIntent, seedIntent.Id).Value;

            (rebuilt.State, rebuilt.Seq, rebuilt.FilledQty).ShouldBe((live.State, live.Seq, live.FilledQty), $"seed {seed}");
        }
    }

    [Fact]
    public void The_store_refuses_a_gap_a_fork_and_an_unknown_intent_and_stores_nothing()
    {
        var store = new InMemoryExecEventStore();
        var i = Make();
        store.Start(i);

        store.Append(i.Id, ExecEvent.Move(2, ExecState.Planned, ExecState.Armed, ExecEventSource.Engine, "e", At)).IsFailure.ShouldBeTrue();
        store.Append(i.Id, Move(i, ExecState.Armed)).IsSuccess.ShouldBeTrue();
        store.Append(i.Id, Move(i, ExecState.Cancelled)).IsFailure.ShouldBeTrue();
        store.Append(99, Move(i, ExecState.Armed)).Error.Kind.ShouldBe(ErrorKind.NotFound);
        store.Events(i.Id).Count.ShouldBe(1);
    }
}
