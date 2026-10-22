using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Strategies.Model;

namespace XDrishti.Strategies.BuiltIn;

/// <summary>
/// Opening Range Breakout, version 1: the first built-in rule set (spec P3-03a, D8; doc 07 §3). A bare breakout, approved as such by the owner on 2026-10-18: the filters of doc 07 are added one at a time after the first hand
/// check. Id <c>ORB</c>, intraday, evaluated on 15m or 5m, BUY and SELL, one parameter <c>or_min</c> (the minutes of the opening range, default 15). A 15-minute bar that closes above the high of the first 15 minutes while
/// the bar before it did not is a BUY (SELL: below the low); the level form is used because <c>or_high</c> keeps the history of the session only (ADR 0035), so the close, whose history is continuous, is the one lagged. The
/// stop is the opposite end of the opening range, the exit is one <c>FIXED_RR</c>, entries are accepted from 09:30 to 11:30 and the position leaves at 15:15 at the latest.
/// <para>
/// This builder is the test oracle of the stored built-in (ADR 0056): the embedded seed text must read to a definition equal to what it builds, and the runtime reads the stored text. Pure: builds a new definition on every call.
/// </para>
/// </summary>
public static class OrbV1
{
    /// <summary>The id of the strategy.</summary>
    public const string Id = "ORB";

    /// <summary>The name of the one parameter, the minutes of the opening range.</summary>
    public const string OrMinutesParam = "or_min";

    /// <summary>The name of the parameter that is the target as a multiple of the risk (ADR 0056 D5: declared in the model, read by the exit as a named quantity).</summary>
    public const string RewardRiskParam = "rr";

    /// <summary>The BUY trigger: the close is above the high of the opening range and the bar before was not.</summary>
    public const string BuyTrigger = "close > or_high(or_min) and close[1] <= or_high(or_min)";

    /// <summary>The SELL trigger: the close is below the low of the opening range and the bar before was not.</summary>
    public const string SellTrigger = "close < or_low(or_min) and close[1] >= or_low(or_min)";

    /// <summary>Builds the definition for an opening range of <paramref name="orMinutes"/> minutes and a target of <paramref name="rewardRisk"/> times the risk.</summary>
    public static StrategyDefinition Create(double orMinutes = 15, double rewardRisk = 2.0) => new(
        StrategyDefinition.CurrentSchema,
        Id,
        Style.Intraday,
        [Timeframe.M5, Timeframe.M15],
        [Side.Buy, Side.Sell],
        [new StrategyParam(OrMinutesParam, orMinutes), new StrategyParam(RewardRiskParam, rewardRisk)],
        Rules(BuyTrigger),
        Rules(SellTrigger),
        new TimeRules(new TimeOnly(9, 30), new TimeOnly(11, 30), new TimeOnly(15, 15), null),
        []);

    private static SideRules Rules(string trigger) => new(
        null,
        new ConditionList(ListMode.All, [trigger]),
        null,
        new EntrySpec(EntryKind.AtClose),
        new StopSpec(StopKind.OrOpposite, Minutes: Quantity.Named(OrMinutesParam)),
        [new ExitSpec(ExitPolicy.FixedRr, [new ExitParameter(RewardRiskParam, [Quantity.Named(RewardRiskParam)])])],
        null);
}
