using System.Globalization;

namespace XDrishti.Domain.Shadow;

/// <summary>The key a trade has in the simulator and in the paper record, <c>StrategyId|Symbol|Side|DecisionDate</c> (<c>SimTrade.Key</c>), built from a plan item before the session so that the shadow row and the paper entry of one ticket can be joined (spec CF-08, D4).</summary>
public static class ShadowTradeKey
{
    /// <summary>The key; <paramref name="side"/> is the plan's <c>BUY</c> or <c>SELL</c>, spelt as the simulator spells it (<c>Buy</c>, <c>Sell</c>).</summary>
    public static string For(string strategyId, string symbol, string side, DateOnly decisionDate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        var spelt = side switch
        {
            "BUY" => "Buy",
            "SELL" => "Sell",
            _ => throw new ArgumentOutOfRangeException(nameof(side), "The side must be BUY or SELL."),
        };
        return string.Create(CultureInfo.InvariantCulture, $"{strategyId}|{symbol}|{spelt}|{decisionDate:yyyy-MM-dd}");
    }
}
