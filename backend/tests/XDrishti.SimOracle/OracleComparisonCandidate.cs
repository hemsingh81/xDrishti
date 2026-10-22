namespace XDrishti.SimOracle;

/// <summary>A candidate of a comparison for the oracle: its name, its closed trades and the level of its fixed target in R, or null when its exit is not a level (a trail).</summary>
/// <param name="Name">The name.</param>
/// <param name="Trades">The closed trades, open-at-end ones left out.</param>
/// <param name="TargetR">The fixed target in R, or null.</param>
public sealed record OracleComparisonCandidate(string Name, IReadOnlyList<OracleComparisonTrade> Trades, double? TargetR);
