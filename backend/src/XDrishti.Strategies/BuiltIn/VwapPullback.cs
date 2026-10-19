using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Strategies.Model;

namespace XDrishti.Strategies.BuiltIn;

/// <summary>
/// VWAP pullback: the second built-in rule set (spec P3-05; doc 07 §3). THE RULE SET IS THE OWNER'S PROPOSAL, approved as written for the first run; nothing here is evidence of an edge. Id <c>VWAP_PULLBACK</c>, intraday, evaluated on
/// 15m only, BUY and SELL written out by hand. In an up-trend (the hourly EMA(20) above the EMA(50), the last closed daily close above its EMA(20)), after two closes above the session VWAP, a dip bar that makes a lower low and comes
/// back to within <c>pull_atr</c> × ATR(14) of VWAP, and a bar that closes above VWAP and its own open, is a BUY (SELL: the mirror). The stop is the lowest low of the last two bars (<c>swing_low</c>, no buffer), the exit one
/// <c>FIXED_RR</c>, entries are accepted from 10:00 (the first bar the two lags can be known) to 14:30, and the position leaves at 15:15 at the latest. Only the level form is used: <c>vwap</c> keeps the history of the session only
/// (ADR 0035), so its lags are unknown on the first bars and unknown never fires.
/// <para>
/// This builder is the interim source of the built-in until P3-02d seeds it as stored text (ADR 0002). Pure: builds a new definition on every call.
/// </para>
/// </summary>
public static class VwapPullback
{
    /// <summary>The id of the strategy.</summary>
    public const string Id = "VWAP_PULLBACK";

    /// <summary>The name of the parameter that is the distance of the dip's low from VWAP, in ATRs.</summary>
    public const string PullAtrParam = "pull_atr";

    /// <summary>BUY setup 0: the hourly trend is up.</summary>
    public const string BuyHourlyTrend = "tf(\"1h\").ema(20) > tf(\"1h\").ema(50)";

    /// <summary>BUY setup 1: the last closed daily close is above its EMA(20).</summary>
    public const string BuyDailyTrend = "tf(\"1D\").close > tf(\"1D\").ema(20)";

    /// <summary>SELL setup 0: the hourly trend is down.</summary>
    public const string SellHourlyTrend = "tf(\"1h\").ema(20) < tf(\"1h\").ema(50)";

    /// <summary>SELL setup 1: the last closed daily close is below its EMA(20).</summary>
    public const string SellDailyTrend = "tf(\"1D\").close < tf(\"1D\").ema(20)";

    /// <summary>BUY trigger 0: the trigger bar closes above VWAP and above its own open.</summary>
    public const string BuyTriggerBar = "close > vwap and close > open";

    /// <summary>BUY trigger 1: the two bars before closed above VWAP (as it stood at their own closes).</summary>
    public const string BuyTwoClosesAbove = "close[1] > vwap[1] and close[2] > vwap[2]";

    /// <summary>BUY trigger 2: the dip bar made a lower low and its low came to within <c>pull_atr</c> ATRs of VWAP.</summary>
    public const string BuyDip = "low[1] < low[2] and low[1] <= vwap[1] + pull_atr * atr(14)";

    /// <summary>SELL trigger 0: the trigger bar closes below VWAP and below its own open.</summary>
    public const string SellTriggerBar = "close < vwap and close < open";

    /// <summary>SELL trigger 1: the two bars before closed below VWAP.</summary>
    public const string SellTwoClosesBelow = "close[1] < vwap[1] and close[2] < vwap[2]";

    /// <summary>SELL trigger 2: the rally bar made a higher high and its high came to within <c>pull_atr</c> ATRs of VWAP.</summary>
    public const string SellRally = "high[1] > high[2] and high[1] >= vwap[1] - pull_atr * atr(14)";

    /// <summary>Builds the definition for a dip of <paramref name="pullAtr"/> ATRs and a target of <paramref name="rewardRisk"/> times the risk.</summary>
    public static StrategyDefinition Create(double pullAtr = 0.5, double rewardRisk = 2.0) => new(
        StrategyDefinition.CurrentSchema,
        Id,
        Style.Intraday,
        [Timeframe.M15],
        [Side.Buy, Side.Sell],
        [new StrategyParam(PullAtrParam, pullAtr)],
        Rules([BuyHourlyTrend, BuyDailyTrend], [BuyTriggerBar, BuyTwoClosesAbove, BuyDip], StopKind.SwingLow, rewardRisk),
        Rules([SellHourlyTrend, SellDailyTrend], [SellTriggerBar, SellTwoClosesBelow, SellRally], StopKind.SwingHigh, rewardRisk),
        new TimeRules(new TimeOnly(10, 0), new TimeOnly(14, 30), new TimeOnly(15, 15), null),
        []);

    private static SideRules Rules(string[] setup, string[] trigger, StopKind stop, double rewardRisk) => new(
        new ConditionList(ListMode.All, setup),
        new ConditionList(ListMode.All, trigger),
        null,
        new EntrySpec(EntryKind.AtClose),
        new StopSpec(stop, Lookback: Quantity.Of(2)),
        [new ExitSpec(ExitPolicy.FixedRr, [new ExitParameter("rr", [Quantity.Of(rewardRisk)])])],
        null);
}
