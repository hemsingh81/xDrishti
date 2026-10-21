namespace XDrishti.SimOracle;

/// <summary>
/// A trailing exit as the oracle reads it (P4-03, D1 to D7): the kind, the closed bars of the evaluation timeframe with the value the rule reads (the ATR, the N-bar extreme or the EMA; a NaN is a value that was not ready), the ATR multiple
/// and the activation of <see cref="OracleTrailKind.Atr"/>.
/// </summary>
/// <param name="Kind">Which trail.</param>
/// <param name="Bars">The closed bars, ascending by close instant.</param>
/// <param name="Multiple">The ATR multiple (<see cref="OracleTrailKind.Atr"/> only).</param>
/// <param name="ActivateAtRr">The progress at a bar's close, in initial risks, that switches the trail on (<see cref="OracleTrailKind.Atr"/> only).</param>
public sealed record OracleTrail(OracleTrailKind Kind, IReadOnlyList<OracleTrailBar> Bars, double Multiple = 0, double ActivateAtRr = 0);
