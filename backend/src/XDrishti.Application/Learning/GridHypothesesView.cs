namespace XDrishti.Application.Learning;

/// <summary>Grid proposals for one parameter.</summary>
/// <param name="Parameter">The parameter.</param>
/// <param name="Current">The baseline run's value.</param>
/// <param name="Values">The proposed values, ascending; each can be proposed as a grid hypothesis.</param>
/// <param name="Note">What this is and is not.</param>
public sealed record GridHypothesesView(string Parameter, string Current, IReadOnlyList<string> Values, string Note);
