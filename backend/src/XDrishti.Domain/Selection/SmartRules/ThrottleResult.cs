namespace XDrishti.Domain.Selection;

/// <summary>The equity-curve throttle's answer with its numbers (spec P7-06).</summary>
/// <param name="Status">Applied, unavailable or off.</param>
/// <param name="Code">Stable code.</param>
/// <param name="RiskMultiplier">1, or the cut while the equity is below its average.</param>
/// <param name="Equity">Cumulative R after the last counted trade, if computed.</param>
/// <param name="Average">Average of the last N equity points, if computed.</param>
/// <param name="TradesCounted">Real taken trades closed before the decision date.</param>
/// <param name="Reason">In words, why it is inactive or what it did.</param>
public sealed record ThrottleResult(RuleStatus Status, string Code, double RiskMultiplier, double? Equity, double? Average, int TradesCounted, string Reason);
