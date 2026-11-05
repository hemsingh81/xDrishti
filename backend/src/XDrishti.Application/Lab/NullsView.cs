namespace XDrishti.Application.Lab;

/// <summary>
/// The random-entry null of one Lab verdict (CF-07, D1): the real mean net R against the means of <see cref="Draws"/> seeds, each a full set of random-entry trades with the real trades' instrument, session, side, risk fraction, target multiple and costs.
/// The reading is <see cref="PValue"/> against <see cref="Threshold"/>: <see cref="Status"/> is <c>pass</c> only when the p-value is at most the threshold; the percentiles are displayed, not a second test. <c>unavailable</c> is never a pass and
/// <see cref="PValue"/> is then null.
/// </summary>
/// <param name="OosTrades">Out-of-sample trades of the verdict.</param>
/// <param name="Trades">How many of them were restated with random entries (the real mean is the mean of these).</param>
/// <param name="Draws">Seeds asked for.</param>
/// <param name="ValidSeeds">Seeds with at least one random trade.</param>
/// <param name="RealMeanR">Mean net R of the restated real trades; null when no trade could be restated.</param>
/// <param name="RandomMeanR">Mean of the seed means.</param>
/// <param name="P05">5th percentile of the seed means.</param>
/// <param name="P50">Median of the seed means.</param>
/// <param name="P95">95th percentile of the seed means.</param>
/// <param name="P99">99th percentile of the seed means.</param>
/// <param name="Percentile">The configured percentile (95), which sets the level of the test.</param>
/// <param name="PercentileValue">The seed means at that percentile.</param>
/// <param name="CountAtLeast">Seeds whose mean is at least the real mean.</param>
/// <param name="PValue"><c>(CountAtLeast + 1) / (ValidSeeds + 1)</c>.</param>
/// <param name="Threshold">The level of the test, <c>1 - percentile / 100</c>.</param>
/// <param name="Comparison">How the value is read against the threshold (<c>&lt;=</c>).</param>
/// <param name="Status"><c>pass</c>, <c>fail</c> or <c>unavailable</c>.</param>
/// <param name="Reason">One sentence for the reading.</param>
/// <param name="DroppedShare">Share of the (trade, seed) draws dropped after the redraws.</param>
/// <param name="Redraws">Redraws spent over all seeds.</param>
/// <param name="Seed">The seed of the run for this null, as text.</param>
public sealed record RandomEntryView(
    int OosTrades,
    int Trades,
    int Draws,
    int ValidSeeds,
    double? RealMeanR,
    double? RandomMeanR,
    double? P05,
    double? P50,
    double? P95,
    double? P99,
    double Percentile,
    double? PercentileValue,
    int CountAtLeast,
    double? PValue,
    double Threshold,
    string Comparison,
    string Status,
    string Reason,
    double DroppedShare,
    int Redraws,
    string Seed);

/// <summary>
/// The chance tests of a Lab verdict (CF-07, ADR 0208), shown beside PASS/MARGINAL/FAIL: report-only unless the owner turns <c>Enforce</c> on, then a null that is not a pass caps a PASS at MARGINAL and never raises an outcome. The placebo set, the mirror
/// gate and the plateau gate of the spec are not built yet and have no field here.
/// </summary>
/// <param name="Mode">The mode when it was computed.</param>
/// <param name="Enforce">Whether the null could cap the outcome.</param>
/// <param name="Settings">The text of the settings that produced the figures.</param>
/// <param name="SettingsHash">SHA-256 of <paramref name="Settings"/>.</param>
/// <param name="RandomEntry">The random-entry null.</param>
/// <param name="Capped">True when the outcome was capped at MARGINAL by an enforced null.</param>
/// <param name="Basis">What the null is made over: the development window, the in-sample walk and the cost sheet.</param>
/// <param name="Notes">The limits of the figure and the reasons trades were left out.</param>
public sealed record NullsView(string Mode, bool Enforce, string Settings, string SettingsHash, RandomEntryView RandomEntry, bool Capped, string Basis, IReadOnlyList<string> Notes);
