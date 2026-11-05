namespace XDrishti.Domain.Lab;

/// <summary>
/// The random-entry null of CF-07 (D1) for one set of trades. The percentiles and the mean are those of the seed means (empirical); <see cref="PValue"/> is <c>(CountAtLeast + 1) / (ValidSeeds + 1)</c> and the test is <c>PValue &lt;= Threshold</c>
/// only (the 95th percentile is displayed, not a second test). The figures are null when the status is <see cref="NullStatus.Unavailable"/>, which says why in <see cref="Reason"/>.
/// </summary>
/// <param name="Trades">Real trades the null was made for.</param>
/// <param name="Draws">Seeds asked for.</param>
/// <param name="ValidSeeds">Seeds with at least one random trade.</param>
/// <param name="RealMeanR">Mean net R of the real trades.</param>
/// <param name="RandomMeanR">Mean of the seed means.</param>
/// <param name="P05">5th percentile of the seed means.</param>
/// <param name="P50">Median of the seed means.</param>
/// <param name="P95">95th percentile of the seed means (the configured percentile is <see cref="PercentileValue"/>).</param>
/// <param name="P99">99th percentile of the seed means.</param>
/// <param name="PercentileLevel">The configured percentile (95 by default).</param>
/// <param name="PercentileValue">The seed means at the configured percentile.</param>
/// <param name="CountAtLeast">Seeds whose mean is at least the real mean.</param>
/// <param name="PValue"><c>(CountAtLeast + 1) / (ValidSeeds + 1)</c>.</param>
/// <param name="Threshold">The level of the test, <c>1 - percentile / 100</c>.</param>
/// <param name="Status">Pass, fail or unavailable.</param>
/// <param name="Reason">One sentence for the reading.</param>
public sealed record RandomEntryResult(
    int Trades,
    int Draws,
    int ValidSeeds,
    double RealMeanR,
    double? RandomMeanR,
    double? P05,
    double? P50,
    double? P95,
    double? P99,
    double PercentileLevel,
    double? PercentileValue,
    int CountAtLeast,
    double? PValue,
    double Threshold,
    NullStatus Status,
    string Reason);
