using XDrishti.Domain.Research;
using XDrishti.Indicators;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Simulation.Walking;
using XDrishti.Strategies.BuiltIn;
using XDrishti.Strategies.Model;

namespace XDrishti.Simulation.Tests.Scanning;

/// <summary>Criteria 2 and 3 of P4-01: the tape the scanner records and the one function that reads it (D1 to D3).</summary>
public sealed class ExitTapeTests
{
    private static ExitTape TapeOf(params long[] closes)
    {
        var tape = new ExitTape(["f"]);
        foreach (var close in closes)
        {
            tape.Append(close, close / 10.0, [close * 2.0]);
        }

        return tape;
    }

    [Fact]
    public void The_empty_tape_is_shared_has_no_row_and_cannot_be_written_to()
    {
        ExitTape.Empty.ShouldBeSameAs(ExitTape.Empty);
        ExitTape.Empty.Count.ShouldBe(0);
        ExitTape.Empty.Features.ShouldBeEmpty();
        ExitTape.Empty.LastClosedAtOrBefore(long.MaxValue).ShouldBe(-1);
        Should.Throw<InvalidOperationException>(() => ExitTape.Empty.Append(1, 1, []));
    }

    [Theory]
    [InlineData(99, -1)]
    [InlineData(100, 0)]
    [InlineData(101, 0)]
    [InlineData(199, 0)]
    [InlineData(200, 1)]
    [InlineData(5_000, 1)]
    [InlineData(9_999, 1)]
    [InlineData(10_000, 2)]
    [InlineData(long.MaxValue, 2)]
    public void The_last_closed_bar_is_found_before_the_first_at_a_close_inside_a_bar_between_two_sessions_and_after_the_last(long instant, int expected)
    {
        // closes at 100 and 200 (one session) and 10,000 (the next)
        TapeOf(100, 200, 10_000).LastClosedAtOrBefore(instant).ShouldBe(expected);
    }

    [Fact]
    public void The_last_closed_bar_equals_the_brute_force_answer_for_every_instant_of_a_small_series()
    {
        var closes = new long[] { 3, 4, 9, 10, 17, 18, 40 };
        var tape = TapeOf(closes);

        for (long instant = -2; instant <= 45; instant++)
        {
            var expected = Array.FindLastIndex(closes, c => c <= instant);
            tape.LastClosedAtOrBefore(instant).ShouldBe(expected, $"instant {instant}");
        }
    }

    [Fact]
    public void A_row_must_close_after_the_last_and_have_one_value_per_feature()
    {
        var tape = TapeOf(10, 20);

        Should.Throw<InvalidOperationException>(() => tape.Append(20, 1, [1]));
        Should.Throw<InvalidOperationException>(() => tape.Append(30, 1, []));
        tape.Value(0, 1).ShouldBe(40.0);
        tape.Close(1).ShouldBe(2.0);
        tape.IndexOf("f").ShouldBe(0);
        tape.IndexOf("g").ShouldBe(-1);
        Should.Throw<ArgumentOutOfRangeException>(() => tape.CloseEpoch(2));
    }

    private sealed class RecordingRule : IExitRule
    {
        public List<int> Seen { get; } = [];

        public void OnBar(in ExitView view, ExitTape tape, int bar, List<ExitAction> actions) => Seen.Add(bar);
    }

    private static Position Start(long fillEpoch) => new(
        Cand.B(), fillEpoch, true, 100m, 100m, EntryFill.AtOpen, 98m, null, 2m, Slip.Of(SlippageConfig.None, 0.05m), false, false, FillBasis.Exact, false);

    [Fact]
    public void A_rule_at_the_minute_that_starts_at_t_sees_only_bars_closed_at_or_before_t_each_once_in_order_and_never_the_bar_of_the_decision()
    {
        // the first row closes at the fill instant (the decision bar): never read; the others close at 120, 180, 300
        var tape = TapeOf(60, 120, 180, 300);
        var recorder = new RecordingRule();
        var legs = new LegState(Start(60), new ExitContext(recorder, tape));
        var minute = new Minute(100m, 101m, 99m);

        for (long t = 60; t <= 360; t += 10)
        {
            legs.Read(t);
            legs.Take(minute, t, openThroughStop: false);
            recorder.Seen.ShouldAllBe(bar => tape.CloseEpoch(bar) <= t, $"t = {t}");
            recorder.Seen.ShouldBe([.. Enumerable.Range(1, 3).Where(bar => tape.CloseEpoch(bar) <= t)], $"t = {t}");
        }

        recorder.Seen.ShouldBe([1, 2, 3]);
    }

    [Fact]
    public void A_scanner_whose_exits_read_a_feature_records_one_row_for_each_closed_bar_with_the_value_as_of_that_close()
    {
        var bars = Fixtures.OrbDays.Single(f => f.Name == "F1").Bars;
        var definition = OrbV1.Create().WithBuy(b => b with { Exits = [new ExitSpec(ExitPolicy.FixedRr, [new ExitParameter("rr", [Quantity.Of(2)])]), new ExitSpec(ExitPolicy.AtrTrail, [new ExitParameter("mult", [Quantity.Of(2)]), new ExitParameter("activate_at_rr", [Quantity.Of(1)])])] });
        var side = Scan.Compile(definition, Side.Buy);
        var reads = side.Exits.SelectMany(e => e.Reads).Distinct().ToList();
        reads.ShouldNotBeEmpty("the trail reads the ATR");

        var scanner = Scan.Run(side, bars);

        // the frame stepped alone: the value of the feature at each step where the evaluation timeframe closed a bar
        var frame = FeatureFrame.Create(side.Features, new FrameOptions(null, Math.Max(side.HistoryDepth, 1))).Value;
        var handle = frame.HandleOf(reads[0]);
        var expected = new List<(long Close, double BarClose, double Value)>();
        foreach (var bar in bars)
        {
            frame.Step(bar);
            if (frame.BarClosed(side.EvaluationTimeframe))
            {
                frame.TryLastClosed(side.EvaluationTimeframe, out var closed);
                expected.Add((frame.LastBarTime + 60, closed.Bar.Close, frame[handle]));
            }
        }

        scanner.Tape.Features.Count.ShouldBe(reads.Count);
        scanner.Tape.Count.ShouldBe(expected.Count);
        scanner.Tape.Count.ShouldBeGreaterThan(10);
        for (var k = 0; k < expected.Count; k++)
        {
            scanner.Tape.CloseEpoch(k).ShouldBe(expected[k].Close);
            scanner.Tape.Close(k).ShouldBe(expected[k].BarClose);
            scanner.Tape.Value(0, k).Equals(expected[k].Value).ShouldBeTrue($"bar {k}: {scanner.Tape.Value(0, k)} against {expected[k].Value}");
        }

        Enumerable.Range(0, scanner.Tape.Count).Count(k => double.IsNaN(scanner.Tape.Value(0, k))).ShouldBeGreaterThan(0, "the first bars are warm-up: NaN, which a rule reads as no new level");
        scanner.Tape.Count.ShouldBe((int)scanner.Counters.DecisionSteps);
    }

    [Fact]
    public void A_scanner_whose_exits_read_nothing_has_the_shared_empty_tape()
    {
        var scanner = Scan.Run(OrbV1.Create(), Side.Buy, Fixtures.OrbDays.Single(f => f.Name == "F1").Bars);

        scanner.Tape.ShouldBeSameAs(ExitTape.Empty);
    }
}
