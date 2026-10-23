namespace XDrishti.Domain.Learning;

/// <summary>The band of expected rolling expectancy, from the reference period (P6-09): the mean of R and its spread, and the lower edge and clear level for the mean of a window.</summary>
/// <param name="ReferenceTrades">Trades the band was made from.</param>
/// <param name="Mean">Mean R of the reference.</param>
/// <param name="Sd">Sample standard deviation of R of the reference.</param>
/// <param name="Lower">The mean of a window of trades below this is a breach.</param>
/// <param name="ClearLevel">A breach clears when the mean of a window is back at this level (half way between <paramref name="Lower"/> and <paramref name="Mean"/>).</param>
public sealed record ExpectancyBand(int ReferenceTrades, double Mean, double Sd, double Lower, double ClearLevel);
