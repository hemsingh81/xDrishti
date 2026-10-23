namespace XDrishti.Domain.Selection;

/// <summary>One filter's answer with the numbers behind it.</summary>
/// <param name="Code">The stable code, see <see cref="FilterCodes"/>.</param>
/// <param name="Outcome">Pass, fail or unavailable.</param>
/// <param name="Value">The number that was judged; null when there was none.</param>
/// <param name="Threshold">The limit it was judged against; null for a yes or no filter.</param>
/// <param name="Detail">One line in words.</param>
public sealed record FilterResult(string Code, FilterOutcome Outcome, double? Value, double? Threshold, string Detail);
