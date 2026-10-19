using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Strategies.BuiltIn;
using XDrishti.Strategies.Model;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>
/// The strategies of the fixtures that are not ORB v1 (spec P3-03a, D12): variants of ORB with one list changed (F19, F19b, F20, F23) and the test-only strategies <c>STALE_TEST</c> (F18b), <c>CROSS_VWAP_TEST</c> (F21)
/// and <c>STOP_NOT_READY_TEST</c> (F22). Written here because this project cannot reference the tests of the strategy library; a small duplication of its helper is accepted.
/// </summary>
internal static class TestStrategies
{
    public static readonly TimeRules OrbWindow = new(new TimeOnly(9, 30), new TimeOnly(11, 30), new TimeOnly(15, 15), null);

    public static ConditionList All(params string[] items) => new(ListMode.All, items);

    public static ExitSpec Rr(double rr) => new(ExitPolicy.FixedRr, [new ExitParameter("rr", [Quantity.Of(rr)])]);

    public static StrategyDefinition WithBuy(this StrategyDefinition definition, Func<SideRules, SideRules> change) => definition with { Buy = change(definition.Buy!) };

    public static StrategyDefinition OrbBuyOnly() => OrbV1.Create() with { Sides = [Side.Buy], Sell = null };

    /// <summary>F19: ORB whose BUY side is invalidated when the bar's low goes under the low of the opening range.</summary>
    public static StrategyDefinition InvalidateOnLowUnderRange() => OrbV1.Create().WithBuy(b => b with { Invalidate = All("low < or_low(or_min)") });

    /// <summary>F20: ORB whose BUY side needs the clock to be 10:00 or later.</summary>
    public static StrategyDefinition SetupAfterTen() => OrbV1.Create().WithBuy(b => b with { Setup = All("time >= \"10:00\"") });

    /// <summary>F23: ORB whose BUY side is invalidated by a condition that reads two closed bars back (unknown on the first two bars).</summary>
    public static StrategyDefinition InvalidateUnknownEarly() => OrbV1.Create().WithBuy(b => b with { Invalidate = All("close[2] < 99.50") });

    /// <summary>F18b: no entry window at all, so that every decision step is eligible and the stale step is observable.</summary>
    public static StrategyDefinition StaleTest() => new(
        StrategyDefinition.CurrentSchema,
        "STALE_TEST",
        Style.Intraday,
        [Timeframe.M15],
        [Side.Buy],
        [],
        new SideRules(null, All("close > 0"), null, new EntrySpec(EntryKind.AtClose), new StopSpec(StopKind.OrOpposite, Minutes: Quantity.Of(15)), [Rr(2.0)], null),
        null,
        new TimeRules(null, null, new TimeOnly(15, 15), null),
        []);

    /// <summary>F21: a crossing of the close over the VWAP, which is unknown on the first bar of a session (ADR 0035).</summary>
    public static StrategyDefinition CrossVwap() => new(
        StrategyDefinition.CurrentSchema,
        "CROSS_VWAP_TEST",
        Style.Intraday,
        [Timeframe.M15],
        [Side.Buy],
        [],
        new SideRules(null, All("crosses_above(close, vwap)"), null, new EntrySpec(EntryKind.AtClose), new StopSpec(StopKind.OrOpposite, Minutes: Quantity.Of(15)), [Rr(2.0)], null),
        null,
        OrbWindow,
        []);

    /// <summary>F22: the trigger is always true and the stop is the lowest low of three bars, which has no value for the first two.</summary>
    public static StrategyDefinition StopNotReady() => new(
        StrategyDefinition.CurrentSchema,
        "STOP_NOT_READY_TEST",
        Style.Intraday,
        [Timeframe.M15],
        [Side.Buy],
        [],
        new SideRules(null, All("close > 0"), null, new EntrySpec(EntryKind.AtClose), new StopSpec(StopKind.SwingLow, Lookback: Quantity.Of(3)), [Rr(2.0)], null),
        null,
        OrbWindow,
        []);

    /// <summary>
    /// <c>SWING_1H_TEST</c> (P3-03d, fixture R): a swing BUY on the 1h bar, trigger <c>close &gt; or_high(60)</c>, the stop on the low of the first hour (<c>or_opposite</c>, 60 minutes), one <c>FIXED_RR 2.0</c>, first entry 10:15, last entry 14:15,
    /// held up to <paramref name="maxHoldSessions"/> sessions (3 in the fixture).
    /// </summary>
    public static StrategyDefinition Swing1h(int maxHoldSessions = 3) => new(
        StrategyDefinition.CurrentSchema,
        "SWING_1H_TEST",
        Style.Swing,
        [Timeframe.H1],
        [Side.Buy],
        [],
        new SideRules(null, All("close > or_high(60)"), null, new EntrySpec(EntryKind.AtClose), new StopSpec(StopKind.OrOpposite, Minutes: Quantity.Of(60)), [Rr(2.0)], null),
        null,
        new TimeRules(new TimeOnly(10, 15), new TimeOnly(14, 15), null, maxHoldSessions),
        []);

    /// <summary><c>SWING_1H_EMA_TEST</c> (criterion 18): history-dependent, trigger <c>close &gt; ema(10)</c> on the 1h bar, the stop and the hold of fixture R; it reads the history, so a scanner started at <c>From</c> sees other candidates.</summary>
    public static StrategyDefinition Swing1hEma() => Swing1h() with
    {
        Id = "SWING_1H_EMA_TEST",
        Buy = Swing1h().Buy! with { Trigger = All("close > ema(10)") },
    };

    /// <summary>
    /// <c>SWING_DAILY_TEST</c> (P3-03e, DF1 and DF2): evaluated on the 1D bar, a trigger that is always true, the stop on the low of the daily bar (<c>bar_low</c>), one <c>FIXED_RR 2.0</c>, held up to five sessions, no entry window (a daily bar cannot
    /// have one). <paramref name="trigger"/> is the trigger condition (<c>close &gt; 0</c> unless a test needs candidates on some days only).
    /// </summary>
    public static StrategyDefinition SwingDaily(string trigger = "close > 0", int maxHoldSessions = 5) => new(
        StrategyDefinition.CurrentSchema,
        "SWING_DAILY_TEST",
        Style.Swing,
        [Timeframe.D1],
        [Side.Buy],
        [],
        new SideRules(null, All(trigger), null, new EntrySpec(EntryKind.AtClose), new StopSpec(StopKind.BarLow), [Rr(2.0)], null),
        null,
        new TimeRules(null, null, null, maxHoldSessions),
        []);

    /// <summary>A swing strategy on the timeframe (a level stop, a fixed reward-risk, held up to five sessions): what the daily evaluation and the swing style refusals are tested with.</summary>
    public static StrategyDefinition Swing(Timeframe timeframe) => new(
        StrategyDefinition.CurrentSchema,
        "SWING_TEST",
        Style.Swing,
        [timeframe],
        [Side.Buy],
        [],
        new SideRules(null, All("close > 0"), null, new EntrySpec(EntryKind.AtClose), new StopSpec(StopKind.SwingLow, Lookback: Quantity.Of(10)), [Rr(3.0)], null),
        null,
        new TimeRules(null, null, null, 5),
        []);
}
