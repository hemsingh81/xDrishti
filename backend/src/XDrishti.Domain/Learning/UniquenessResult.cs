namespace XDrishti.Domain.Learning;

/// <summary>The average uniqueness of labels that overlap in time (CF-10 part 10c-1, spec D5).</summary>
/// <param name="Uniqueness">One value per label in <c>(0, 1]</c>: the mean over its bars of one over the number of labels spanning that bar.</param>
/// <param name="Weights">The uniqueness scaled to mean one.</param>
/// <param name="EffectiveCount">The sum of the uniqueness: how many independent labels the set is worth (the labels given, when none overlap).</param>
public sealed record UniquenessResult(IReadOnlyList<double> Uniqueness, IReadOnlyList<double> Weights, double EffectiveCount);
