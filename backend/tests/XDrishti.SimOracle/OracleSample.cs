namespace XDrishti.SimOracle;

/// <summary>
/// The figures of one sample of trades in the oracle (spec P4-05, D6). A ratio of an empty sample is NaN here (the engine says null); the comparison maps one to the other.
/// </summary>
/// <param name="Count">Trades.</param>
/// <param name="Wins">Trades with a result above 0.</param>
/// <param name="WinRate">Wins over trades.</param>
/// <param name="Mean">Mean result.</param>
/// <param name="Median">Median result.</param>
/// <param name="DownsideDeviation">Square root of the mean of the squared negative parts, target 0.</param>
/// <param name="Total">Sum of the results.</param>
/// <param name="ProfitFactor">Gains over the absolute losses; NaN without a loss.</param>
/// <param name="AverageHold">Mean minutes held.</param>
/// <param name="ByReason">Trades by exit reason.</param>
public sealed record OracleSample(int Count, int Wins, double WinRate, double Mean, double Median, double DownsideDeviation, double Total, double ProfitFactor, double AverageHold, IReadOnlyDictionary<OracleExitReason, int> ByReason);
