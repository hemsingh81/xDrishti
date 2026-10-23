namespace XDrishti.Domain.Selection.SmartRules;

/// <summary>Everything the smart rules read besides the ranked list. A null part means the data does not exist.</summary>
/// <param name="Gate">Market figures before the decision date.</param>
/// <param name="TakenTrades">Real taken trades with their R.</param>
/// <param name="Correlations">Pair correlations before the decision date.</param>
/// <param name="OpenBook">Open trades and holdings.</param>
public sealed record SmartInputs(MarketGateInput? Gate = null, IReadOnlyList<TakenTrade>? TakenTrades = null, CorrelationTable? Correlations = null, IReadOnlyList<OpenPosition>? OpenBook = null);
