namespace XDrishti.Domain.Selection;

/// <summary>One portfolio constraint's answer for one candidate, with the numbers (as they would be after the candidate is added).</summary>
/// <param name="Code">The stable code, see <see cref="FillCodes"/>.</param>
/// <param name="Outcome">Pass, fail or unavailable; unavailable blocks like a fail.</param>
/// <param name="Value">The figure judged; null when there was none.</param>
/// <param name="Limit">The limit; null when there was none.</param>
/// <param name="Detail">One line in words.</param>
public sealed record FillCheck(string Code, FilterOutcome Outcome, double? Value, double? Limit, string Detail);
