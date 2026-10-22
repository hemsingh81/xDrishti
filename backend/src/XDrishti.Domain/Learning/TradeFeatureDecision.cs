using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Learning;

/// <summary>What the extractor is told about one trade: only what was known at the decision. The exit, the result and the fill are not here, so they cannot leak into a feature.</summary>
/// <param name="TradeKey">The <see cref="SimTrade.Key"/>.</param>
/// <param name="Timeframe">The evaluation timeframe of the trade.</param>
/// <param name="DecisionEpochSeconds">The decision instant: the end of the minute that closed the evaluation bar. Bars that start at or after it are never read.</param>
/// <param name="Side">Buy or sell.</param>
/// <param name="StopPrice">The stop level given by the candidate at the decision.</param>
/// <param name="TargetPrice">The target level the plan sets at the decision; null without one.</param>
public sealed record TradeFeatureDecision(string TradeKey, Timeframe Timeframe, long DecisionEpochSeconds, Side Side, double StopPrice, double? TargetPrice);
