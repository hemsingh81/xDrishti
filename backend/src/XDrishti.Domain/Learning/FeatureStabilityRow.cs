namespace XDrishti.Domain.Learning;

/// <summary>One feature's direction of effect in each fold of one candidate (CF-10 part 10b).</summary>
/// <param name="Index">The feature's position in <see cref="ValidationRow.Features"/>.</param>
/// <param name="Signs">By used fold: +1 when a higher value pushed the model's margin up, -1 when down, 0 when no direction could be read.</param>
/// <param name="Flips">The folds on the minority side of the direction.</param>
/// <param name="Judged">The folds that gave a direction.</param>
/// <param name="Verdict">The verdict.</param>
public sealed record FeatureStabilityRow(int Index, IReadOnlyList<int> Signs, int Flips, int Judged, StabilityVerdict Verdict);
