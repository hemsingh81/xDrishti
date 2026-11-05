namespace XDrishti.Domain.Learning;

/// <summary>Whether a model has earned its rung of the ladder (CF-10 part 10b), with every reason it has not.</summary>
/// <param name="Scorer">The scorer's name in the report.</param>
/// <param name="Rung">The rung it sits on.</param>
/// <param name="Events">The smaller class of the rows it is trained on.</param>
/// <param name="FeatureCount">The features it reads.</param>
/// <param name="ProbabilityBeatsBelow">The probability that it beats the rung below on the paired bootstrap, or null when that figure is not available (an unavailable figure is never a pass).</param>
/// <param name="Earned">True when the rung is earned: no reason against.</param>
/// <param name="Reasons">Why not, in a fixed order; empty when earned.</param>
public sealed record LadderAssessment(string Scorer, LadderRung Rung, int Events, int FeatureCount, double? ProbabilityBeatsBelow, bool Earned, IReadOnlyList<string> Reasons);
