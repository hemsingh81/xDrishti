namespace XDrishti.Domain.Selection.SmartRules;

/// <summary>A trade the system really took (not a simulated one), closed with a result in R.</summary>
/// <param name="TradeId">Stable id; the order of equal dates.</param>
/// <param name="ClosedOn">Date it closed.</param>
/// <param name="R">Result in R.</param>
public sealed record TakenTrade(string TradeId, DateOnly ClosedOn, double R);
