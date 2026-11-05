namespace XDrishti.Domain.Lab;

/// <summary>
/// The rank of the real strategy among the placebo shapes (CF-07, D2). <see cref="RealRank"/> is 1 plus the comparable placebos whose mean is at or above the real mean (a tie goes against the strategy); <see cref="PValue"/> is
/// <c>rank / (comparable + 1)</c>; the test passes if and only if <c>PValue &lt;= Threshold</c> and at least <see cref="MinComparable"/> placebos are comparable. Below that it is <see cref="NullStatus.Unavailable"/> (inconclusive), never a pass.
/// </summary>
/// <param name="Shapes">Shapes asked for (20).</param>
/// <param name="Comparable">Shapes that could be compared.</param>
/// <param name="MinComparable">The fewest comparable shapes that make the test conclusive (19 by default).</param>
/// <param name="RealMeanR">Mean net R of the real trades.</param>
/// <param name="RealRank">The rank of the real strategy; null when unavailable.</param>
/// <param name="PValue"><c>RealRank / (Comparable + 1)</c>; null when unavailable.</param>
/// <param name="Threshold">The level of the test (0.05 by default).</param>
/// <param name="Status">Pass, fail or unavailable.</param>
/// <param name="Reason">One sentence for the reading.</param>
/// <param name="Rows">One row per shape, in the fixed order.</param>
public sealed record PlaceboSetResult(int Shapes, int Comparable, int MinComparable, double RealMeanR, int? RealRank, double? PValue, double Threshold, NullStatus Status, string Reason, IReadOnlyList<PlaceboRow> Rows);
