namespace XDrishti.Application.Lab;

/// <summary>One placebo shape in the ranking of a Lab verdict (CF-07, D2).</summary>
/// <param name="Shape">The shape name (<c>mirror</c>, <c>shift_+15</c>, <c>date_shift_-3</c>, <c>instrument_roll_2</c>, ...).</param>
/// <param name="Trades">Real trades that got a placebo trade of this shape.</param>
/// <param name="MeanR">Mean net R of those placebo trades after the same charges; null when none.</param>
/// <param name="Rank">Its place among the strategy and the comparable placebos, 1 the best; null when it is not comparable.</param>
/// <param name="Comparable">True when enough trades were placed for it to be ranked.</param>
/// <param name="Reason">Why it is not comparable; null when it is.</param>
public sealed record PlaceboRowView(string Shape, int Trades, double? MeanR, int? Rank, bool Comparable, string? Reason);

/// <summary>
/// The placebo set of one Lab verdict (CF-07, D2): the real strategy against 20 fixed shapes (the mirror, entries 15 to 60 minutes later, the same clock time on sessions 1 to 5 away either side, the signals reassigned to the next 1 to 5 instruments). The strategy must rank
/// first: <see cref="PValue"/> is <c>rank / (comparable + 1)</c> against <see cref="Threshold"/>; with fewer than <see cref="MinComparable"/> comparable placebos the reading is <c>unavailable</c> (inconclusive) and never a pass.
/// </summary>
/// <param name="Shapes">Shapes asked for.</param>
/// <param name="Comparable">Shapes that could be compared.</param>
/// <param name="MinComparable">The fewest comparable shapes for a conclusive reading.</param>
/// <param name="RealMeanR">Mean net R of the real trades that were restated.</param>
/// <param name="RealRank">The strategy's rank among the comparable placebos and itself; null when unavailable.</param>
/// <param name="PValue"><c>RealRank / (Comparable + 1)</c>; null when unavailable.</param>
/// <param name="Threshold">The level of the test.</param>
/// <param name="Comparison">How the value is read against the threshold (<c>&lt;=</c>).</param>
/// <param name="Status"><c>pass</c>, <c>fail</c> or <c>unavailable</c>.</param>
/// <param name="Reason">One sentence for the reading.</param>
/// <param name="Rows">One row per shape, in the fixed order; empty when nothing could be walked.</param>
public sealed record PlaceboSetView(int Shapes, int Comparable, int MinComparable, double? RealMeanR, int? RealRank, double? PValue, double Threshold, string Comparison, string Status, string Reason, IReadOnlyList<PlaceboRowView> Rows);

/// <summary>
/// The mirror gate of one Lab verdict (CF-07, D3): the net expectancy of the real trades taken the other way round with the same costs must be at most <see cref="Threshold"/> (0). A positive mirror reads "mostly drift". <c>unavailable</c> is never a pass.
/// </summary>
/// <param name="Trades">Real trades that got a mirror trade.</param>
/// <param name="MirrorMeanR">Mean net R of the mirror trades; null when unavailable.</param>
/// <param name="RealMeanR">Mean net R of the real trades, beside it.</param>
/// <param name="Threshold">0.</param>
/// <param name="Comparison">How the value is read against the threshold (<c>&lt;=</c>).</param>
/// <param name="Status"><c>pass</c>, <c>fail</c> or <c>unavailable</c>.</param>
/// <param name="Reason">One sentence for the reading.</param>
public sealed record MirrorView(int Trades, double? MirrorMeanR, double? RealMeanR, double Threshold, string Comparison, string Status, string Reason);
