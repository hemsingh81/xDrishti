using XDrishti.Domain.Market.Bars;
using XDrishti.Simulation.Replay;
using XDrishti.Simulation.Running;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;

namespace XDrishti.Simulation.Tests.Leaks;

/// <summary>
/// No state leaks between instruments (spec P3-08b, Annex B (d)), the dynamic check that complements the architecture rules against mutable statics in <c>XDrishti.Simulation</c>. One compiled <see cref="Simulator"/> (and one
/// <see cref="PreparedStrategy"/> through the replayer) is run over the sequence A of 100 seeded series, then over B (100 other series, other generator), then over A again in another order: the digest of every series of the second A equals the digest
/// of the same series in the first. The order is changed on purpose: the same order would repeat every predecessor and a cache of the previous instrument's last value would be invisible after the first series. <b>What this does not test:</b> the engine makes a
/// new frame for each scanner and never calls <c>FeatureFrame.Reset()</c>, so "fresh frame against <c>Reset</c>" is not a property of the loop; the <c>Reset</c> equivalence is covered in <c>XDrishti.Indicators.Tests</c>
/// (<c>DeterminismAndResetTests</c>, <c>FrameColdStartTests</c>). The negative control is a scanner that caches the previous instrument's last value (the previous series' last close widens the first minute of the next one).
/// </summary>
public sealed class StateLeakTests(ITestOutputHelper output)
{
    private const int Count = 100;

    private sealed record Subject(string Name, IReadOnlyList<Bar[]> A, IReadOnlyList<Bar[]> B, Func<Bar[], string> Run);

    private static readonly Lazy<IReadOnlyList<Subject>> All = new(Build);

    private static List<Subject> Build()
    {
        IReadOnlyList<Bar[]> intradayA = [.. Enumerable.Range(1, Count).Select(i => SeededSeries.Of((ulong)i))];
        IReadOnlyList<Bar[]> intradayB = [.. Enumerable.Range(201, Count).Select(i => SeededSeries.Of((ulong)i))];
        IReadOnlyList<Bar[]> replayA = [.. SeededReplay.All.Select(s => s.Bars.ToArray())];
        IReadOnlyList<Bar[]> replayB = [.. Enumerable.Range(1, Count).Select(i => SeededSessions.Of((ulong)i).Bars)];
        var subjects = new List<Subject>();
        foreach (var (name, definition, timeframe) in new (string, XDrishti.Strategies.Model.StrategyDefinition, Timeframe)[]
        {
            ("loop ORB", OrbV1.Create(), Timeframe.M15),
            ("loop F20 setup", TestStrategies.SetupAfterTen(), Timeframe.M15),
            ("loop F21 per-session", TestStrategies.CrossVwap(), Timeframe.M15),
            ("loop SWING_1H", TestStrategies.Swing1h(), Timeframe.H1),
        })
        {
            var simulator = Sim.Create(definition, timeframe);
            subjects.Add(new Subject(name, intradayA, intradayB, bars => Leak.Digest(Sim.Run(simulator, bars))));
        }

        foreach (var (name, definition) in new (string, XDrishti.Strategies.Model.StrategyDefinition)[] { ("replay ORB", OrbV1.Create()), ("replay ORB+ema10", Rep.OrbWithEma10()) })
        {
            var prepared = Rep.Prepare(definition);
            subjects.Add(new Subject(name, replayA, replayB, bars => RunText.Replay(Rep.Replay(prepared, bars, SessionClock.DateOf(bars[0].EpochSeconds), SessionClock.DateOf(bars[^1].EpochSeconds), calendar: TestCalendars.DefaultWeek()))));
        }

        return subjects;
    }

    /// <summary>The same series in another order: a seeded shuffle.</summary>
    private static int[] Shuffled(int salt) => [.. Enumerable.Range(0, Count).OrderBy(i => new SeededSeries((ulong)((i + 1) * 40_503) + (ulong)salt).Next())];

    /// <summary>The digests of the series of A in the first pass, of the second A in the other order, and how many series differ.</summary>
    private static (int Differ, int Total, int Active) Passes(Subject s, Func<Bar[], string> run)
    {
        var first = s.A.Select(run).ToList();
        foreach (var bars in s.B)
        {
            _ = run(bars);
        }

        var second = new string[Count];
        foreach (var i in Shuffled(s.Name.Length))
        {
            second[i] = run(s.A[i]);
        }

        return (Enumerable.Range(0, Count).Count(i => first[i] != second[i]), Count, first.Count(d => d.Contains("-> trade", StringComparison.Ordinal) || d.Contains("trade schema=", StringComparison.Ordinal)));
    }

    [Fact]
    public void One_prepared_strategy_run_over_A_then_B_then_A_in_another_order_gives_every_series_the_same_result_both_times()
    {
        var lines = new List<string>();
        foreach (var s in All.Value)
        {
            var (differ, total, active) = Passes(s, s.Run);
            lines.Add($"{s.Name}: {total} series, {active} with a trade, {differ} differ");
            differ.ShouldBe(0, s.Name);
            if (s.Name is "loop ORB" or "replay ORB")
            {
                active.ShouldBeGreaterThanOrEqualTo(20, s.Name + " (non-vacuity)");
            }
        }

        output.WriteLine(string.Join("\n", lines));
    }

    /// <summary>The leak: a scanner that kept the last close of the previous instrument. The first minute of the next series is widened to include it, so the opening range and the first decisions see a price that is not theirs.</summary>
    private static Func<Bar[], string> Leaky(Func<Bar[], string> run)
    {
        double? cached = null;
        return bars =>
        {
            var seen = (Bar[])bars.Clone();
            if (cached is { } last)
            {
                seen[0] = seen[0] with { High = Math.Max(seen[0].High, last), Low = Math.Min(seen[0].Low, last) };
            }

            cached = bars[^1].Close;
            return run(seen);
        };
    }

    [Fact]
    public void Control_a_scanner_that_caches_the_previous_instruments_last_value_is_reported_on_at_least_ten_series_while_the_honest_run_reports_none()
    {
        var lines = new List<string>();
        foreach (var s in All.Value.Where(s => s.Name is "loop ORB" or "replay ORB"))
        {
            var leaky = Passes(s, Leaky(s.Run)).Differ;
            var honest = Passes(s, s.Run).Differ;
            lines.Add($"{s.Name}: leaky scanner differs on {leaky} of {Count} series, honest {honest}");
            honest.ShouldBe(0, s.Name);
            leaky.ShouldBeGreaterThanOrEqualTo(10, s.Name);
        }

        output.WriteLine(string.Join("\n", lines));
    }
}
