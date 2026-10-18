using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Strategies.Model;

namespace XDrishti.Strategies.Tests.Support;

/// <summary>
/// Test-only C# builders of strategy definitions: the valid definitions every table of the tests starts from (a near miss is a valid one, a failing row is the same with one change, made with <c>with</c>), the example of doc 11
/// and the bare ORB. The built-ins are builders here until P3-02d seeds them as stored text; the ORB text is owned by the P3-03 spec and this is a copy of it.
/// </summary>
internal static class Defs
{
    public static readonly TimeRules IntradayRules = new(new TimeOnly(9, 30), new TimeOnly(11, 30), new TimeOnly(15, 15), null);

    public static ConditionList All(params string[] items) => new(ListMode.All, items);

    public static ConditionList Any(params string[] items) => new(ListMode.Any, items);

    public static ExitParameter P(string name, params double[] values) => new(name, [.. values.Select(Quantity.Of)]);

    public static ExitParameter P(string name, params Quantity[] values) => new(name, values);

    public static ExitSpec FixedRr(params double[] rr) => new(ExitPolicy.FixedRr, [P("rr", rr)]);

    public static ExitSpec PartialTrail(double bookPct = 50, double t1Rr = 1.0, double trailAtrMult = 2.0) =>
        new(ExitPolicy.PartialTrail, [P("book_pct", bookPct), P("t1_rr", t1Rr), P("trail_atr_mult", trailAtrMult)]);

    public static StopSpec FixedPct(double pct = 1.0) => new(StopKind.FixedPct, Pct: Quantity.Of(pct));

    /// <summary>A valid BUY-and-SELL intraday strategy on 5m, 15m and 1h with one condition per section and the simplest stop and exit.</summary>
    public static SideRules SimpleRules(Side side = Side.Buy) => new(
        null,
        All(side == Side.Buy ? "close > open" : "close < open"),
        null,
        new EntrySpec(EntryKind.AtClose),
        side == Side.Buy ? FixedPct() : FixedPct(),
        [FixedRr(2.0)],
        null);

    public static StrategyDefinition Basic(string id = "BASIC") => new(
        StrategyDefinition.CurrentSchema,
        id,
        Style.Intraday,
        [Timeframe.M5, Timeframe.M15, Timeframe.H1],
        [Side.Buy, Side.Sell],
        [],
        SimpleRules(Side.Buy),
        SimpleRules(Side.Sell),
        IntradayRules,
        []);

    /// <summary>A valid swing strategy on 30m, 1h and 1D, BUY only.</summary>
    public static StrategyDefinition Swing(string id = "SWING") => new(
        StrategyDefinition.CurrentSchema,
        id,
        Style.Swing,
        [Timeframe.M30, Timeframe.H1, Timeframe.D1],
        [Side.Buy],
        [new StrategyParam("lookback_days", 10)],
        new SideRules(
            null,
            All("close > highest(20)", "rsi(14) > 50"),
            null,
            new EntrySpec(EntryKind.AtClose),
            new StopSpec(StopKind.SwingLow, Lookback: Quantity.Named("lookback_days")),
            [FixedRr(3.0)],
            null),
        null,
        new TimeRules(null, null, null, 5),
        []);

    /// <summary>The BUY side of VWAP_RSI_TREND as doc 11 §2 writes it: 15m, intraday, <c>sides: [BUY, SELL]</c> and no sell block (criterion 13).</summary>
    public static StrategyDefinition VwapRsiTrend() => new(
        StrategyDefinition.CurrentSchema,
        "VWAP_RSI_TREND",
        Style.Intraday,
        [Timeframe.M15],
        [Side.Buy, Side.Sell],
        [new StrategyParam("rsi_len", 14), new StrategyParam("rsi_min", 55), new StrategyParam("swing_lookback", 5)],
        new SideRules(
            All("time >= \"09:45\"", "tf(\"1h\").ema(20) > tf(\"1h\").ema(50)", "rsi(rsi_len) > rsi_min"),
            All("crosses_above(close, vwap)"),
            null,
            new EntrySpec(EntryKind.AtClose),
            new StopSpec(StopKind.SwingLow, Lookback: Quantity.Named("swing_lookback"), BufferAtr: Quantity.Of(0.1)),
            [FixedRr(2.0), PartialTrail(50, 1.0, 2.0)],
            null),
        null,
        new TimeRules(null, new TimeOnly(14, 30), new TimeOnly(15, 15), null),
        [FilterNames.NotEventDay, FilterNames.MinTurnover]);

    /// <summary>The bare ORB of criterion 14: 15m and 5m, <c>or_min</c> 15, both sides, the stop at the opposite end of the opening range, FIXED_RR 2.0, 09:30 to 11:30, square-off 15:15.</summary>
    public static StrategyDefinition Orb(params Timeframe[] timeframes) => new(
        StrategyDefinition.CurrentSchema,
        "ORB",
        Style.Intraday,
        timeframes.Length == 0 ? [Timeframe.M15, Timeframe.M5] : timeframes,
        [Side.Buy, Side.Sell],
        [new StrategyParam("or_min", 15)],
        OrbSide("close > or_high(or_min) and close[1] <= or_high(or_min)"),
        OrbSide("close < or_low(or_min) and close[1] >= or_low(or_min)"),
        IntradayRules,
        []);

    private static SideRules OrbSide(string trigger) => new(
        null,
        All(trigger),
        null,
        new EntrySpec(EntryKind.AtClose),
        new StopSpec(StopKind.OrOpposite, Minutes: Quantity.Named("or_min")),
        [FixedRr(2.0)],
        null);

    public static StrategyDefinition WithBuy(this StrategyDefinition definition, Func<SideRules, SideRules> change) => definition with { Buy = change(definition.Buy!) };

    public static StrategyDefinition WithSell(this StrategyDefinition definition, Func<SideRules, SideRules> change) => definition with { Sell = change(definition.Sell!) };
}
