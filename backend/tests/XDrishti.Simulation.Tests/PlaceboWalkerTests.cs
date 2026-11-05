using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Replay;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Simulation.Walking;
using XDrishti.Strategies.BuiltIn;

namespace XDrishti.Simulation.Tests;

/// <summary>
/// CF-07 part B, criteria 4 and 5 (ADR 0209): the placebo walk. Each shape is a forced entry (the seam of part A) at the minute the shape asks for, so every outcome is compared with a hand-built forced walk of the same bars: the mirror is the other side at the real decision minute, a shift is
/// that many minutes later, a date shift is the same clock time on a session of the same instrument, a roll is the same signal on the next instrument. None reads a session after the end of the window; what cannot be walked says why and has no number.
/// </summary>
public sealed class PlaceboWalkerTests
{
    private static readonly PreparedStrategy Orb = Rep.Prepare(OrbV1.Create());

    private static readonly DateOnly Monday = Day.Monday;

    private static readonly Guid OtherId = new("00000000-0000-0000-0000-0000000000b2");

    /// <summary>The minute (of the day) that closed the real evaluation bar; the decision is taken at its end.</summary>
    private const int DecisionMinute = (9 * 60) + 45;

    private static DateOnly NextSession(DateOnly date) => date.DayOfWeek == DayOfWeek.Friday ? date.AddDays(3) : date.AddDays(1);

    /// <summary><paramref name="sessions"/> full sessions on consecutive weekdays from Monday, one seeded series per session.</summary>
    private static Bar[] Sessions(int sessions, ulong seed)
    {
        var bars = new List<Bar>();
        var date = Monday;
        for (var s = 0; s < sessions; s++)
        {
            bars.AddRange(SeededSeries.Of(seed + (ulong)(s * 7919)).Where(b => SessionClock.DateOf(b.EpochSeconds) == Monday).Select(b => b with { EpochSeconds = Day.Epoch(date, SessionClock.MinuteOfDay(b.EpochSeconds)) }));
            date = NextSession(date);
        }

        return [.. bars];
    }

    private static DateOnly Session(int n)
    {
        var date = Monday;
        for (var i = 0; i < n; i++)
        {
            date = NextSession(date);
        }

        return date;
    }

    private static InstrumentReplayInput Input(IReadOnlyList<Bar> bars, Guid? id = null, DateOnly? to = null, CostContext? costs = null, SkipReason? preset = null) =>
        Rep.Input(bars, Monday, to ?? SessionClock.DateOf(bars[^1].EpochSeconds), id: id, costs: costs ?? TestCostSheet.Context(), preset: preset);

    private static PlaceboSignal Signal(int id = 0, Guid? instrument = null, DateOnly? date = null, Side side = Side.Buy, double risk = 0.01, double rr = 2) =>
        new(id, instrument ?? Rep.Id, date ?? Session(3), side, risk, rr, CostSegment.IntradayEquity, Day.Epoch(date ?? Session(3), DecisionMinute) + 60);

    private static PlaceboWalkPlan Plan(params PlaceboSignal[] signals) => new(signals, PlaceboShapes.RollOrder(signals.Select(s => s.InstrumentId).Append(Rep.Id).Append(OtherId)));

    private static PlaceboWalkResult Walk(InstrumentReplayInput input, PlaceboWalkPlan plan, PreparedStrategy? strategy = null, IProgress<int>? done = null, CancellationToken? token = null) =>
        PlaceboWalker.Walk(strategy ?? Orb, input, plan, done, token ?? TestContext.Current.CancellationToken);

    private static PlaceboOutcome Of(PlaceboWalkResult result, string shape, int signal = 0) =>
        result.Outcomes.Single(o => o.SignalId == signal && PlaceboShapes.All[o.Shape].Name == shape);

    /// <summary>The independent oracle: a forced entry on the bars of <paramref name="date"/> at <paramref name="minute"/> of the day, costed with the input's sheet; null when the walk makes no trade.</summary>
    private static double? Oracle(InstrumentReplayInput input, DateOnly date, int minute, Side side, double risk, double rr)
    {
        var bars = input.RawBars.Where(b => SessionClock.DateOf(b.EpochSeconds) == date).ToArray();
        var index = Array.FindIndex(bars, b => SessionClock.MinuteOfDay(b.EpochSeconds) == minute);
        var squareOff = Orb.Simulator.CompiledOf(Side.Buy)!.TimeRules.SquareOffMinute!.Value;
        var candidate = EntryOverride.Forced(new InstrumentRef(input.InstrumentId, input.Symbol), Orb.Record.Id, bars, index, side, EntryOverride.StopAt(side, bars[index].Close, risk));
        var trade = EntryOverride.Walk(candidate, bars, new WalkRules(rr, squareOff, Orb.Fills, input.Tick!)).Value.Trade;
        return trade is null ? null : TradeCostApplier.Apply(trade, input.Costs!, CostSegment.IntradayEquity).Value.Trade!.Costing!.RNet;
    }

    [Fact]
    public void The_mirror_is_the_other_side_at_the_real_decision_minute_with_the_same_risk_fraction_and_target()
    {
        var input = Input(Sessions(8, 3));
        var signal = Signal();

        var mirror = Of(Walk(input, Plan(signal)), "mirror");

        mirror.Reason.ShouldBeNull();
        mirror.NetR.ShouldBe(Oracle(input, signal.Date, DecisionMinute, Side.Sell, 0.01, 2));
        Oracle(input, signal.Date, DecisionMinute, Side.Buy, 0.01, 2).ShouldNotBe(mirror.NetR, "the other side is another trade");
    }

    [Theory]
    [InlineData("shift_+15", 15)]
    [InlineData("shift_+30", 30)]
    [InlineData("shift_+45", 45)]
    [InlineData("shift_+60", 60)]
    public void A_shift_keeps_the_side_and_enters_that_many_minutes_after_the_real_decision(string shape, int minutes)
    {
        var input = Input(Sessions(8, 3));
        var signal = Signal(side: Side.Sell);

        var shifted = Of(Walk(input, Plan(signal)), shape);

        shifted.NetR.ShouldBe(Oracle(input, signal.Date, DecisionMinute + minutes, Side.Sell, 0.01, 2));
    }

    [Theory]
    [InlineData("date_shift_+1", 1)]
    [InlineData("date_shift_+5", 5)]
    [InlineData("date_shift_-1", -1)]
    [InlineData("date_shift_-2", -2)]
    public void A_date_shift_is_the_same_clock_time_on_a_session_of_the_same_instrument_that_many_sessions_away(string shape, int sessions)
    {
        var input = Input(Sessions(12, 5));
        var signal = Signal(date: Session(6));

        var shifted = Of(Walk(input, Plan(signal)), shape);

        shifted.NetR.ShouldBe(Oracle(input, Session(6 + sessions), DecisionMinute, Side.Buy, 0.01, 2));
    }

    [Fact]
    public void A_date_shift_with_no_such_session_has_no_number_and_says_why()
    {
        var input = Input(Sessions(4, 5));
        var first = Signal(date: Monday);
        var last = Signal(1, date: Session(3));

        var result = Walk(input, Plan(first, last));

        Of(result, "date_shift_-1").Reason.ShouldNotBeNull().ShouldContain("no bars");
        Of(result, "date_shift_-1").NetR.ShouldBeNull();
        Of(result, "date_shift_+1", 1).NetR.ShouldBeNull();
        Of(result, "date_shift_+1", 0).NetR.ShouldNotBeNull();
    }

    [Fact]
    public void A_shift_past_the_end_of_the_session_has_no_minute_to_decide_at()
    {
        var input = Input(Sessions(4, 5));
        var late = Signal() with { DecisionEpochSeconds = Day.Epoch(Session(3), SessionClock.CloseMinute - 20) + 60 };

        var shifted = Of(Walk(input, Plan(late)), "shift_+30");

        shifted.NetR.ShouldBeNull();
        shifted.Reason.ShouldNotBeNull().ShouldContain("no minute at the shifted decision time");
    }

    [Fact]
    public void An_instrument_roll_is_walked_on_the_next_instrument_in_the_order_and_only_there()
    {
        var a = Input(Sessions(8, 3), Rep.Id);
        var b = Input(Sessions(8, 99), OtherId);
        var signal = Signal();
        var plan = Plan(signal);
        var order = plan.RollOrder;
        var asIndex = order.ToList().IndexOf(Rep.Id);
        var next = order[(asIndex + 1) % order.Count];

        var onA = Walk(a, plan);
        var onB = Walk(b, plan);

        onA.Outcomes.Select(o => PlaceboShapes.All[o.Shape].Name).ShouldNotContain("instrument_roll_1", "a roll to another instrument is walked on that one only");
        onB.Outcomes.Select(o => PlaceboShapes.All[o.Shape].Name).Where(n => n.StartsWith("instrument_roll_", StringComparison.Ordinal)).ShouldBe(PlaceboShapes.All.Where(s => s.Kind == PlaceboKind.RollInstrument && PlaceboShapes.Place(s, signal, order)?.InstrumentId == OtherId).Select(s => s.Name));
        if (next == OtherId)
        {
            Of(onB, "instrument_roll_1").NetR.ShouldBe(Oracle(b, signal.Date, DecisionMinute, Side.Buy, 0.01, 2), "the same signal, the same clock time, on the other instrument's bars and sheet");
        }
    }

    [Fact]
    public void The_outcomes_do_not_depend_on_the_order_instruments_are_walked_and_a_repeat_is_identical()
    {
        var a = Input(Sessions(8, 3), Rep.Id);
        var b = Input(Sessions(8, 99), OtherId);
        var plan = Plan(Signal(0), Signal(1, date: Session(5), side: Side.Sell));

        var ab = Walk(a, plan).Outcomes.Concat(Walk(b, plan).Outcomes).OrderBy(o => o.SignalId).ThenBy(o => o.Shape).ToList();
        var ba = Walk(b, plan).Outcomes.Concat(Walk(a, plan).Outcomes).OrderBy(o => o.SignalId).ThenBy(o => o.Shape).ToList();

        ab.ShouldBe(ba);
        Walk(a, plan).Outcomes.ShouldBe(Walk(a, plan).Outcomes);
        ab.Count.ShouldBe(plan.Signals.Sum(sig => PlaceboShapes.All.Count(shape => PlaceboShapes.Place(shape, sig, plan.RollOrder)?.InstrumentId is { } id && (id == Rep.Id || id == OtherId))), "every placed pair is walked on exactly one of the two instruments");
    }

    [Fact]
    public void No_session_after_the_end_of_the_window_is_read_and_the_bars_after_it_change_nothing()
    {
        var bars = Sessions(10, 5);
        var end = Session(4);
        var cut = Input([.. bars.Where(b => SessionClock.DateOf(b.EpochSeconds) <= end)], to: end);
        var scrambled = Input([.. bars.Select(b => SessionClock.DateOf(b.EpochSeconds) > end ? b with { Open = 9_999, High = 9_999, Low = 9_999, Close = 9_999 } : b)], to: end);
        var plan = Plan(Signal(date: Session(3)));

        var clean = Walk(cut, plan);
        var withLater = Walk(scrambled, plan);

        withLater.Outcomes.ShouldBe(clean.Outcomes);
        Of(clean, "date_shift_+1").NetR.ShouldNotBeNull("the session on the end date is inside");
        Of(clean, "date_shift_+2").NetR.ShouldBeNull("two sessions on is after the end of the window");
        Of(clean, "date_shift_+2").Reason.ShouldNotBeNull().ShouldContain("no bars");
    }

    [Fact]
    public void A_signal_after_the_end_of_the_window_has_no_session_and_no_number()
    {
        var input = Input(Sessions(6, 5), to: Session(2));

        var result = Walk(input, Plan(Signal(date: Session(4))));

        result.Outcomes.Where(o => o.SignalId == 0 && PlaceboShapes.All[o.Shape].Kind != PlaceboKind.RollInstrument).ShouldAllBe(o => o.NetR == null && o.Reason != null);
    }

    [Fact]
    public void What_cannot_be_restated_says_why_for_every_pair_and_has_no_numbers()
    {
        var bars = Sessions(6, 5);
        var plan = Plan(Signal());

        var gross = Walk(Rep.Input(bars, Monday, Session(5), costs: null), plan);
        var skipped = Walk(Input(bars, preset: SkipReason.CsvAdjustedTwice), plan);
        var swing = Walk(Input(bars), plan, Rep.Prepare(TestStrategies.Swing1h(), Timeframe.H1, FillRules.Default));

        gross.Outcomes.ShouldAllBe(o => o.NetR == null && o.Reason!.Contains("gross", StringComparison.Ordinal));
        skipped.Outcomes.ShouldAllBe(o => o.NetR == null && o.Reason!.Contains("csv_adjusted_twice", StringComparison.Ordinal));
        swing.Outcomes.ShouldAllBe(o => o.NetR == null && o.Reason!.Contains("swing", StringComparison.Ordinal));
        gross.Outcomes.ShouldNotBeEmpty();
    }

    [Fact]
    public void A_pair_that_makes_no_trade_has_no_number_and_the_reason_from_the_walker()
    {
        // no minute holds a trade after the real decision: every forced entry is "no fill bar"
        var bars = Sessions(6, 5).Select(b => SessionClock.MinuteOfDay(b.EpochSeconds) > DecisionMinute - 1 ? b with { Volume = 0 } : b).ToArray();

        var result = Walk(Input(bars), Plan(Signal()));

        Of(result, "mirror").NetR.ShouldBeNull();
        Of(result, "mirror").Reason.ShouldNotBeNull().ShouldStartWith("no trade:");
    }

    [Fact]
    public void Each_pair_is_reported_once_and_a_cancel_stops_the_walk_before_the_next_pair()
    {
        var input = Input(Sessions(12, 5));
        var plan = Plan(Signal(date: Session(6)));
        var seen = new List<int>();
        Walk(input, plan, done: new Collect(seen));
        seen.ShouldAllBe(v => v == 1);
        seen.Count.ShouldBe(Walk(input, plan).Outcomes.Count);

        using var cts = new CancellationTokenSource();
        var cancelled = new List<int>();

        Should.Throw<OperationCanceledException>(() => Walk(input, plan, done: new Collect(cancelled, () => cts.Cancel()), token: cts.Token));

        cancelled.ShouldBe([1], "the cancel was seen before the second pair");
        using var already = new CancellationTokenSource();
        already.Cancel();
        var none = new List<int>();
        Should.Throw<OperationCanceledException>(() => Walk(input, plan, done: new Collect(none), token: already.Token));
        none.ShouldBeEmpty();
    }

    [Fact]
    public void A_plan_with_no_signal_on_the_instrument_gives_nothing()
    {
        var stranger = new PlaceboSignal(0, Guid.NewGuid(), Session(3), Side.Buy, 0.01, 2, CostSegment.IntradayEquity, 1);

        Walk(Input(Sessions(4, 5)), new PlaceboWalkPlan([stranger], [stranger.InstrumentId])).Outcomes.ShouldBeEmpty();
    }

    private sealed class Collect(List<int> seen, Action? after = null) : IProgress<int>
    {
        public void Report(int value)
        {
            seen.Add(value);
            after?.Invoke();
        }
    }
}
