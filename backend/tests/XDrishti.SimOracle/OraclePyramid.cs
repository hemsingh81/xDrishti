namespace XDrishti.SimOracle;

/// <summary>
/// The ladder of <c>PYRAMID</c> as the oracle reads it from the rule table (P4-04, D1 to D4 and ADR 0052): the share of the full size entered first, the progress levels in initial risks, the share of each add, how many levels may be used, the
/// closed bars of the evaluation timeframe (the instant each closed and its close) and the full size <c>Q</c> in shares when the run is costed (null for a gross run, where no add is skipped for its quantity). The ATR trail of the combined
/// position is the oracle's <see cref="OracleTrail"/> of kind <see cref="OracleTrailKind.Atr"/> with no activation, set beside this record.
/// </summary>
public sealed record OraclePyramid(decimal InitialShare, IReadOnlyList<decimal> Levels, IReadOnlyList<decimal> Shares, int MaxAdds, IReadOnlyList<OracleTrailBar> Bars, long? FullSize);
