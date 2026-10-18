using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Strategies.Model;

namespace XDrishti.Strategies.Compilation;

/// <summary>
/// The strategy definitions of the compiler warm-up (spec P3-02b, criterion 29): valid intraday and swing strategies on both sides that between them use every entry kind, stop kind, exit policy and overlay, and
/// invalid ones that reach every group of <c>strategy.model.*</c> codes (a stop on the wrong side, a fault of the model, a fault in an item with its path, the problem cap). Written by hand, in the shapes the Lab will
/// write; kept apart from the definitions of the benchmarks, so that a benchmark compiles a strategy the warm-up has never seen. The unit tests prove the coverage.
/// </summary>
internal static class WarmupStrategies
{
    /// <summary>One definition, the evaluation timeframe and the side it is compiled for.</summary>
    internal readonly record struct Case(StrategyDefinition Definition, Timeframe Timeframe, Side Side);

    private static readonly TimeRules IntradayRules = new(new TimeOnly(9, 30), new TimeOnly(11, 30), new TimeOnly(15, 15), null);

    private static ConditionList All(params string[] items) => new(ListMode.All, items);

    private static ConditionList Any(params string[] items) => new(ListMode.Any, items);

    private static ExitParameter P(string name, params double[] values) => new(name, [.. values.Select(Quantity.Of)]);

    private static ExitSpec Rr(params double[] rr) => new(ExitPolicy.FixedRr, [P("rr", rr)]);

    private static ExitSpec PartialTrail() => new(ExitPolicy.PartialTrail, [P("book_pct", 50), P("t1_rr", 1), P("trail_atr_mult", 2), P("atr_period", 14)]);

    private static SideRules Rules(ConditionList trigger, StopSpec stop, params ExitSpec[] exits) =>
        new(null, trigger, null, new EntrySpec(EntryKind.AtClose), stop, exits, null);

    /// <summary>An opening range breakout on both sides: the stop at the opposite end of the range, the minutes a parameter, FIXED_RR with two candidates.</summary>
    private static StrategyDefinition Orb() => new(
        StrategyDefinition.CurrentSchema,
        "WARM_ORB",
        Style.Intraday,
        [Timeframe.M15, Timeframe.M5],
        [Side.Buy, Side.Sell],
        [new StrategyParam("or_min", 15)],
        Rules(All("close > or_high(or_min) and close[1] <= or_high(or_min)"), new StopSpec(StopKind.OrOpposite, Minutes: Quantity.Named("or_min")), Rr(1.5, 2.0)),
        Rules(All("close < or_low(or_min) and close[1] >= or_low(or_min)"), new StopSpec(StopKind.OrOpposite, Minutes: Quantity.Named("or_min")), Rr(1.5, 2.0)),
        IntradayRules,
        []);

    /// <summary>A VWAP trend strategy: all three lists, both modes, a higher-timeframe feature, a parameter in a condition and in a stop, five exit policies, two entry kinds, both overlays, both filters.</summary>
    private static StrategyDefinition VwapTrend() => new(
        StrategyDefinition.CurrentSchema,
        "WARM_VWAP",
        Style.Intraday,
        [Timeframe.M15, Timeframe.M30],
        [Side.Buy, Side.Sell],
        [new StrategyParam("rsi_len", 14), new StrategyParam("rsi_min", 55), new StrategyParam("swing_lookback", 5), new StrategyParam("rsi_max", 45)],
        new SideRules(
            All("time >= \"09:45\"", "tf(\"1h\").ema(20) > tf(\"1h\").ema(50)", "rsi(rsi_len) > rsi_min"),
            Any("crosses_above(close, vwap)", "close > vwap and rising(close, 2)"),
            All("close < open and volume == 0"),
            new EntrySpec(EntryKind.StopAboveHigh),
            new StopSpec(StopKind.SwingLow, Lookback: Quantity.Named("swing_lookback"), BufferAtr: Quantity.Of(0.1), AtrPeriod: Quantity.Of(14)),
            [
                Rr(2.0),
                PartialTrail(),
                new ExitSpec(ExitPolicy.PartialBe, [P("book_pct", 50), P("t1_rr", 1)]),
                new ExitSpec(ExitPolicy.AtrTrail, [P("atr_mult", 2)]),
            ],
            [new OverlaySpec(OverlayKind.TimeStop, [P("min_progress_rr", 0.5), P("intraday_bars", 12)])]),
        new SideRules(
            All("time >= \"09:45\"", "tf(\"1h\").ema(20) < tf(\"1h\").ema(50)", "rsi(rsi_len) < rsi_max"),
            All("crosses_below(close, vwap)"),
            null,
            new EntrySpec(EntryKind.LimitPullback),
            new StopSpec(StopKind.SwingHigh, Lookback: Quantity.Named("swing_lookback"), BufferAtr: Quantity.Of(0.1)),
            [
                Rr(2.0, 3.0),
                new ExitSpec(ExitPolicy.StructureTrail, [P("lookback", 5)]),
                new ExitSpec(ExitPolicy.EmaTrail, [P("ema_len", 20)]),
                new ExitSpec(ExitPolicy.Pyramid, [P("add_at_rr", 1, 2), P("max_adds", 2)]),
            ],
            [new OverlaySpec(OverlayKind.EventExit, [P("days_before_results", 1)])]),
        new TimeRules(null, new TimeOnly(14, 30), new TimeOnly(15, 15), null),
        [FilterNames.NotEventDay, FilterNames.MinTurnover]);

    /// <summary>Small intraday strategies for the stops the others do not use: the bar's own extreme on each side and a fixed percentage.</summary>
    private static StrategyDefinition BarStops() => new(
        StrategyDefinition.CurrentSchema,
        "WARM_BAR",
        Style.Intraday,
        [Timeframe.M30],
        [Side.Buy, Side.Sell],
        [],
        Rules(All("close > open and close > ema(20)"), new StopSpec(StopKind.BarLow, BufferAtr: Quantity.Of(0.2)), Rr(2.0)),
        Rules(All("close < open and close < ema(20)"), new StopSpec(StopKind.BarHigh), Rr(2.0)),
        new TimeRules(null, null, new TimeOnly(15, 15), null),
        []);

    /// <summary>A swing strategy on 1h and 1D: the market context and the expiry calendar, a stop in ATRs, and a fixed percentage.</summary>
    private static StrategyDefinition Swing() => new(
        StrategyDefinition.CurrentSchema,
        "WARM_SWING",
        Style.Swing,
        [Timeframe.H1, Timeframe.D1],
        [Side.Buy],
        [new StrategyParam("breakout_len", 20)],
        new SideRules(
            All("regime() == \"UP_LOW_VOL\"", "is_expiry_day == 0"),
            All("close > highest(breakout_len)", "rsi(14) > 50"),
            null,
            new EntrySpec(EntryKind.AtClose),
            new StopSpec(StopKind.AtrMult, Mult: Quantity.Of(2), AtrPeriod: Quantity.Of(14)),
            [Rr(3.0)],
            null),
        null,
        new TimeRules(null, null, null, 5),
        []);

    /// <summary>The definitions that compile, each with the timeframe and the side it is compiled for.</summary>
    internal static readonly IReadOnlyList<Case> Valid = Array.AsReadOnly<Case>(
    [
        new(Orb(), Timeframe.M15, Side.Buy),
        new(Orb(), Timeframe.M15, Side.Sell),
        new(Orb(), Timeframe.M5, Side.Buy),
        new(VwapTrend(), Timeframe.M15, Side.Buy),
        new(VwapTrend(), Timeframe.M30, Side.Sell),
        new(BarStops(), Timeframe.M30, Side.Buy),
        new(BarStops(), Timeframe.M30, Side.Sell),
        new(Swing(), Timeframe.H1, Side.Buy),
        new(Swing() with { Buy = Swing().Buy! with { Stop = new StopSpec(StopKind.FixedPct, Pct: Quantity.Of(2)) } }, Timeframe.D1, Side.Buy),

        // more warnings than are listed: 101 redundant timeframes, so the compile succeeds and the list ends with the note that some were dropped
        new(Orb() with { Buy = Orb().Buy! with { Setup = All([.. Enumerable.Repeat("tf(\"15m\").close > 1", 100)]), Trigger = All("tf(\"15m\").close > 1") } }, Timeframe.M15, Side.Buy),
    ]);

    /// <summary>The definitions that do not compile: a stop on the wrong side, a fault of every group of the model, faults in items with their paths, and more problems than are listed.</summary>
    internal static readonly IReadOnlyList<Case> Invalid = Array.AsReadOnly<Case>(
    [
        // a stop of the other side, and a stop with its fields wrong
        new(BarStops() with { Buy = BarStops().Buy! with { Stop = new StopSpec(StopKind.BarHigh) } }, Timeframe.M30, Side.Buy),
        new(Orb() with { Buy = Orb().Buy! with { Stop = new StopSpec(StopKind.SwingLow, Lookback: Quantity.Named("or_mn"), Pct: Quantity.Of(1), Minutes: Quantity.Of(7)) } }, Timeframe.M15, Side.Buy),
        new(Orb() with { Buy = Orb().Buy! with { Stop = new StopSpec(StopKind.FixedPct, Timeframe.H1, Pct: Quantity.Of(99)) } }, Timeframe.M15, Side.Buy),
        new(Orb() with { Buy = Orb().Buy! with { Stop = new StopSpec(StopKind.OrOpposite, Minutes: Quantity.Of(20), BufferAtr: Quantity.Of(9)) } }, Timeframe.M15, Side.Buy),

        // the definition: schema, id, style, timeframes, sides, params
        new(Orb() with { Schema = 2, Id = "1x", Style = (Style)7, Timeframes = [Timeframe.M15, Timeframe.M15, Timeframe.M1, Timeframe.D1], Sides = [Side.Buy, Side.Buy] }, Timeframe.M30, Side.Sell),
        new(Orb() with { Style = Style.Swing, Timeframes = [Timeframe.M5], Sides = [Side.Sell, Side.Buy] }, Timeframe.M15, Side.Buy),
        new(Orb() with { Params = [new StrategyParam("or_min", 15), new StrategyParam("or_min", double.NaN), new StrategyParam("RSI", 1), new StrategyParam("close", 1), new StrategyParam("d", 1), new StrategyParam("e", 1)], Sides = [Side.Sell], Buy = null }, Timeframe.M15, Side.Buy),
        new(Orb() with { Timeframes = [], Sides = [] }, Timeframe.M15, Side.Buy),

        // the sections
        new(Orb() with { Buy = new SideRules(All(), All(), All(), null, null, [], null) }, Timeframe.M15, Side.Buy),
        new(Orb() with { Buy = null }, Timeframe.M15, Side.Buy),
        new(Orb() with { Sell = null }, Timeframe.M15, Side.Sell),
        new(Orb() with { Buy = Orb().Sell! with { Trigger = null } }, Timeframe.M15, Side.Buy),

        // the exits and the overlays
        new(Orb() with { Buy = Orb().Buy! with { Exits = [Rr(2), Rr(3), new ExitSpec(ExitPolicy.PartialTrail, [P("book_pct", 100), P("book_pct", 5), P("trail", 1)]), new ExitSpec(ExitPolicy.Pyramid, [P("Add", 1), new ExitParameter("x", [Quantity.Named("zz")])]), new ExitSpec(ExitPolicy.FixedRr, [P("rr", 0)])], Overlays = [new OverlaySpec(OverlayKind.TimeStop, []), new OverlaySpec(OverlayKind.TimeStop, [])] } }, Timeframe.M15, Side.Buy),

        // time rules and filters
        new(Orb() with { TimeRules = new TimeRules(new TimeOnly(11, 30), new TimeOnly(9, 30), new TimeOnly(15, 45), 3), Filters = ["not_event_dy", FilterNames.MinTurnover, FilterNames.MinTurnover] }, Timeframe.M15, Side.Buy),
        new(Swing() with { TimeRules = new TimeRules(new TimeOnly(9, 30), null, new TimeOnly(15, 15), 9) }, Timeframe.D1, Side.Buy),
        new(Orb() with { TimeRules = null }, Timeframe.M15, Side.Buy),
        new(Swing() with { Sides = [Side.Buy, Side.Sell], TimeRules = null }, Timeframe.H1, Side.Buy),

        // faults inside items, with their paths: names, syntax, arguments, types, a daily clock
        new(Orb() with { Buy = Orb().Buy! with { Setup = All("closee > 1", "rsi(14 >", "close"), Trigger = All("rsi(14, 3, 4) > 55", "time >= \"9:45\"", "close and rsi(14)") } }, Timeframe.M15, Side.Buy),

        // more problems than are listed
        new(Orb() with { Buy = Orb().Buy! with { Setup = All([.. Enumerable.Repeat("closee > 1", 101)]) } }, Timeframe.M15, Side.Buy),
    ]);
}
