namespace XDrishti.SimOracle;

/// <summary>
/// The test-only exit rule of P4-01 (D14) as the oracle reads it: at each closed bar of <see cref="Bars"/> the stop moves to the bar's low (a buy; the high for a sell) when that is tighter, and the first bar whose close is beyond
/// the entry by one initial risk takes <see cref="Share"/> of the position out. With <see cref="AskLoose"/> the rule also asks, at each bar, for a stop one risk looser than the bar's extreme, which must be ignored and counted.
/// </summary>
/// <param name="Bars">The closed bars of the evaluation timeframe, ascending by close instant.</param>
/// <param name="Share">The share of the maximum quantity the partial exit takes.</param>
/// <param name="AskLoose">Whether the rule also asks for a looser stop at each bar.</param>
public sealed record OracleLadder(IReadOnlyList<OracleLadderBar> Bars, decimal Share, bool AskLoose = false);
