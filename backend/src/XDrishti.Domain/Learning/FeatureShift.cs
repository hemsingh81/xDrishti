namespace XDrishti.Domain.Learning;

/// <summary>How far one feature's test rows moved from its training rows, worst fold (CF-10 part 10b).</summary>
/// <param name="Index">The feature's position in <see cref="ValidationRow.Features"/>.</param>
/// <param name="MaxPsi">The largest population stability index over the folds, or null when no fold could be read.</param>
/// <param name="Fold">The fold that gave it (null with <paramref name="MaxPsi"/>).</param>
/// <param name="Level">The level of <paramref name="MaxPsi"/> (null when it is null).</param>
public sealed record FeatureShift(int Index, double? MaxPsi, int? Fold, PsiLevel? Level);
