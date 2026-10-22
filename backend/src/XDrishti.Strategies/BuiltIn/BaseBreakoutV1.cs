using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Strategies.Model;

namespace XDrishti.Strategies.BuiltIn;

/// <summary>
/// Base breakout, version 1: the third built-in rule set (spec P3-06; doc 07 §3). THE RULE SET IS THE OWNER'S PROPOSAL, approved as written for the first run; nothing here is evidence of an edge. Id <c>BASE_BREAKOUT</c>, swing, evaluated on
/// 1D only, BUY only. In an up-trend (the last closed week closes above its 10-week average, the daily close above its 50-day average), a day whose close is a new <c>base_len</c>-session closing high (a tie counts) out of a base whose closes span at
/// most <c>base_pct</c> percent, on at least <c>vol_x</c> times the mean volume of the previous 20 sessions, is entered at the next session's open within the entry limit. The stop is the lowest low of the last <c>base_len</c> sessions
/// (<c>swing_low</c>, no buffer), the exit one <c>FIXED_RR</c>, and the position leaves at the close of its fifth session at the latest. No lag on a call is used (ISS-24), so the base is measured on the closes of the window that ends today.
/// <para>
/// This builder is the test oracle of the stored built-in (ADR 0056): the embedded seed text must read to a definition equal to what it builds, and the runtime reads the stored text. Pure: builds a new definition on every call.
/// </para>
/// </summary>
public static class BaseBreakoutV1
{
    /// <summary>The id of the strategy.</summary>
    public const string Id = "BASE_BREAKOUT";

    /// <summary>The name of the parameter that is the sessions of the base window (also the lookback of the stop).</summary>
    public const string BaseLenParam = "base_len";

    /// <summary>The name of the parameter that is the widest span of the closes of the window, as % of the lowest.</summary>
    public const string BasePctParam = "base_pct";

    /// <summary>The name of the parameter that is the least multiple of the volume of the 20 sessions before.</summary>
    public const string VolXParam = "vol_x";

    /// <summary>The name of the parameter that is the target as a multiple of the risk (ADR 0056 D5).</summary>
    public const string RewardRiskParam = "rr";

    /// <summary>The sessions a position is held at most.</summary>
    public const int MaxHoldSessions = 5;

    /// <summary>R1, setup: the last closed week closes above its 10-week average.</summary>
    public const string WeeklyTrend = "tf(\"1W\").close > tf(\"1W\").sma(10)";

    /// <summary>R2, setup: the daily close is above its 50-day average.</summary>
    public const string DailyTrend = "close > sma(50)";

    /// <summary>R3, setup: the closes of the last <c>base_len</c> sessions span at most <c>base_pct</c> percent of the lowest.</summary>
    public const string TightBase = "(highest(base_len, source=close) - lowest(base_len, source=close)) / lowest(base_len, source=close) * 100 <= base_pct";

    /// <summary>R4, setup: today's volume is at least <c>vol_x</c> times the mean of the previous 20 sessions.</summary>
    public const string Volume = "rel_volume(20) >= vol_x";

    /// <summary>R5, trigger: today's close is a new <c>base_len</c>-session closing high (a tie counts).</summary>
    public const string Breakout = "close >= highest(base_len, source=close)";

    /// <summary>Builds the definition for a base of <paramref name="baseLen"/> sessions spanning at most <paramref name="basePct"/> percent, <paramref name="volX"/> times the volume and a target of <paramref name="rewardRisk"/> times the risk.</summary>
    public static StrategyDefinition Create(double baseLen = 20, double basePct = 8, double volX = 1.5, double rewardRisk = 2.0) => new(
        StrategyDefinition.CurrentSchema,
        Id,
        Style.Swing,
        [Timeframe.D1],
        [Side.Buy],
        [new StrategyParam(BaseLenParam, baseLen), new StrategyParam(BasePctParam, basePct), new StrategyParam(VolXParam, volX), new StrategyParam(RewardRiskParam, rewardRisk)],
        new SideRules(
            new ConditionList(ListMode.All, [WeeklyTrend, DailyTrend, TightBase, Volume]),
            new ConditionList(ListMode.All, [Breakout]),
            null,
            new EntrySpec(EntryKind.AtClose),
            new StopSpec(StopKind.SwingLow, Lookback: Quantity.Named(BaseLenParam)),
            [new ExitSpec(ExitPolicy.FixedRr, [new ExitParameter(RewardRiskParam, [Quantity.Named(RewardRiskParam)])])],
            null),
        null,
        new TimeRules(null, null, null, MaxHoldSessions),
        []);
}
