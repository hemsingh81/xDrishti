namespace XDrishti.Domain.Learning;

/// <summary>The direction checks of one candidate scorer (CF-10 part 10b).</summary>
/// <param name="Scorer">The candidate's name.</param>
/// <param name="Explained">False when the scorer could not explain its predictions (then every feature is unavailable).</param>
/// <param name="Features">One row per feature, in order.</param>
public sealed record CandidateStability(string Scorer, bool Explained, IReadOnlyList<FeatureStabilityRow> Features);
