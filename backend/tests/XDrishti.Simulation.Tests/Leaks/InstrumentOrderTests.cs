using XDrishti.Domain.Research;
using XDrishti.Simulation.Replay;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;

namespace XDrishti.Simulation.Tests.Leaks;

/// <summary>
/// Instrument-order independence through the replay and the assembly (spec P3-08b, Annex B (c)). A universe of 6 to 12 seeded instruments is replayed in the order given, reversed and in three seeded permutations, with one prepared strategy: the replay of
/// each instrument (its record, counters, trades and open positions, every field as text) is the same in every order and the same as the replay of that instrument alone (no result depends on which others are present), the assembled totals and the set of
/// trades are the same, and the run key is a function of the universe in run order: the same order gives the same key, and the lines of the key text other than the <c>i=</c> lines are the same in every order, the <c>i=</c> lines being the same set.
/// <b>The key does depend on the order, by design</b> (P3-04: "one <c>i=</c> per instrument in run order"; the order of a basket is its position): a permuted universe is another run with another key, and the test asserts that too, so a key that silently became
/// order-free would be seen. The negative control is a universe-dependent fill: each trade's entry is averaged with the previous trade's entry across instruments (a shared last price), which changes the per-instrument result with the order.
/// </summary>
public sealed class InstrumentOrderTests(ITestOutputHelper output)
{
    private const int Universes = 14;

    private static readonly PreparedStrategy Strategy = Rep.Prepare(OrbV1.Create());

    private static Guid IdOf(int poolIndex) => new(poolIndex, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]);

    private static readonly DateOnly From = TestCalendars.Mon;

    private static readonly DateOnly To = SeededReplay.All.Max(s => s.Dates[^1]);

    /// <summary>The members of a universe, as pool indices: 6 to 12 of the 100 series, chosen by the seed.</summary>
    private static int[] Members(int universe)
    {
        var rng = new SeededSeries((ulong)universe * 6_364_136_223_846_793_005UL);
        return [.. Enumerable.Range(0, SeededReplay.Count).OrderBy(_ => rng.Next()).Take(6 + (universe % 7))];
    }

    private static int[][] Orders(int[] members, int universe)
    {
        var orders = new List<int[]> { members, members.Reverse().ToArray() };
        for (var k = 1; k <= 3; k++)
        {
            var rng = new SeededSeries(((ulong)universe * 1_000_003UL) + (ulong)k);
            orders.Add([.. members.OrderBy(_ => rng.Next())]);
        }

        return [.. orders];
    }

    private static InstrumentReplay Replay(int poolIndex) =>
        InstrumentReplayer.Replay(Strategy, Rep.Input(SeededReplay.All[poolIndex].Bars, From, To, id: IdOf(poolIndex), symbol: "S" + poolIndex, calendar: TestCalendars.DefaultWeek()));

    private static SimulationRun Assemble(int[] order, IReadOnlyList<InstrumentReplay> replays)
    {
        var universe = new UniverseRecord(UniverseKind.InstrumentList, null, null, UniverseLabels.InstrumentsAsListed, [.. order.Select(i => new UniverseMember(IdOf(i), "S" + i))]);
        var header = new RunHeader(Strategy.Record, SimulationEngine.Version, From, To, ReplayOptions.Default, false, Strategy.Fills.ToCanonicalJson(), universe, Rep.DefaultCalendar);
        return RunAssembler.Assemble(header, replays);
    }

    private static (string Head, List<string> Instruments) KeyLines(SimulationRun run)
    {
        var header = new RunHeader(run.Strategy, run.EngineVersion, run.From, run.To, run.Options, run.AcknowledgeCsvAdjusted, run.Fills, run.Universe, Rep.DefaultCalendar);
        var lines = RunAssembler.KeyText(header, run.Instruments, run.CalendarExceptions).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        CanonicalText.Sha256Hex(string.Join('\n', lines) + "\n").ShouldBe(run.RunKey);
        return (string.Join('\n', lines.Where(l => !l.StartsWith("i=", StringComparison.Ordinal))), [.. lines.Where(l => l.StartsWith("i=", StringComparison.Ordinal)).Order(StringComparer.Ordinal)]);
    }

    [Fact]
    public void A_universe_in_any_order_gives_the_same_result_for_every_instrument_the_same_trades_and_totals_and_a_key_that_follows_the_order()
    {
        var solo = new Dictionary<int, string>();
        long replays = 0, trades = 0, orderedKeysDifferent = 0, orderedPairs = 0;
        var sizes = new List<int>();
        for (var u = 1; u <= Universes; u++)
        {
            var members = Members(u);
            sizes.Add(members.Length);
            var givenRun = default(SimulationRun);
            var givenTexts = new Dictionary<int, string>();
            foreach (var order in Orders(members, u))
            {
                var done = order.Select(i => (Index: i, Replay: Replay(i))).ToList();
                replays += done.Count;
                var run = Assemble(order, [.. done.Select(d => d.Replay)]);
                if (givenRun is null)
                {
                    givenRun = run;
                    foreach (var (index, replay) in done)
                    {
                        givenTexts[index] = RunText.Replay(replay);
                        if (!solo.TryGetValue(index, out var alone))
                        {
                            solo[index] = alone = RunText.Replay(Replay(index));
                        }

                        givenTexts[index].ShouldBe(alone, $"universe {u}: instrument {index} among others and alone");
                    }

                    trades += run.Trades.Count;
                    KeyLines(run);
                    continue;
                }

                foreach (var (index, replay) in done)
                {
                    RunText.Replay(replay).ShouldBe(givenTexts[index], $"universe {u}: instrument {index} in another order");
                }

                run.Trades.Select(t => RunText.Trade(t.Trade)).Order(StringComparer.Ordinal).ShouldBe(givenRun.Trades.Select(t => RunText.Trade(t.Trade)).Order(StringComparer.Ordinal), $"universe {u}: the set of trades");
                run.OpenAtEnd.Select(t => RunText.Trade(t.Trade)).Order(StringComparer.Ordinal).ShouldBe(givenRun.OpenAtEnd.Select(t => RunText.Trade(t.Trade)).Order(StringComparer.Ordinal), $"universe {u}: the open positions");
                RunText.Totals(run.Totals).ShouldBe(RunText.Totals(givenRun.Totals), $"universe {u}: the totals");
                var (head, lines) = KeyLines(run);
                var (givenHead, givenLines) = KeyLines(givenRun);
                head.ShouldBe(givenHead, $"universe {u}: the key text other than the instrument lines");
                lines.ShouldBe(givenLines, $"universe {u}: the set of instrument lines");
                orderedPairs++;
                orderedKeysDifferent += run.RunKey != givenRun.RunKey ? 1 : 0;
            }

            // the same order twice is the same key
            var again = Assemble(members, [.. members.Select(Replay)]);
            again.RunKey.ShouldBe(givenRun!.RunKey, $"universe {u}: the same order twice");
        }

        output.WriteLine($"{Universes} universes of {sizes.Min()} to {sizes.Max()} instruments in 5 orders: {replays} replays, {trades} trades in the given-order runs; run key differs in {orderedKeysDifferent} of {orderedPairs} other-order runs (by design: the key follows the run order)");
        trades.ShouldBeGreaterThanOrEqualTo(100);
        orderedKeysDifferent.ShouldBeGreaterThanOrEqualTo(orderedPairs - 2);
    }

    /// <summary>The control's fill: the entry of each trade is averaged with the last entry seen anywhere before it in the run (a shared last price), the instruments in the order of the run.</summary>
    private static List<string> LeakyRun(int[] order)
    {
        var last = double.NaN;
        var texts = new List<string>();
        foreach (var index in order)
        {
            var replay = Replay(index);
            var trades = replay.Trades.Select(t =>
            {
                var entry = double.IsNaN(last) ? t.EntryPrice : (t.EntryPrice + last) / 2;
                last = t.EntryPrice;
                return t with { EntryPrice = entry };
            }).ToList();
            texts.Add(index + ": " + string.Join("\n", trades.Select(RunText.Trade)));
        }

        return [.. texts.Order(StringComparer.Ordinal)];
    }

    [Fact]
    public void Control_a_fill_that_uses_a_last_price_shared_across_instruments_is_reported_on_at_least_ten_universes_while_the_honest_replay_reports_none()
    {
        int reported = 0, honest = 0;
        for (var u = 1; u <= Universes; u++)
        {
            var members = Members(u);
            var orders = Orders(members, u);
            var given = LeakyRun(orders[0]);
            reported += orders.Skip(1).Any(o => !LeakyRun(o).SequenceEqual(given)) ? 1 : 0;
            var honestGiven = orders[0].Select(i => i + ": " + string.Join("\n", Replay(i).Trades.Select(RunText.Trade))).Order(StringComparer.Ordinal).ToList();
            honest += orders.Skip(1).Any(o => !o.Select(i => i + ": " + string.Join("\n", Replay(i).Trades.Select(RunText.Trade))).Order(StringComparer.Ordinal).SequenceEqual(honestGiven)) ? 1 : 0;
        }

        output.WriteLine($"shared-last-price control: reported on {reported} of {Universes} universes; honest replay {honest}");
        honest.ShouldBe(0);
        reported.ShouldBeGreaterThanOrEqualTo(10);
    }
}
