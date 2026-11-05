namespace XDrishti.Domain.Research;

/// <summary>The placebo walk of one instrument: one <see cref="PlaceboOutcome"/> for each (signal, shape) pair whose placement is on that instrument.</summary>
/// <param name="Outcomes">The outcomes, in plan order then shape order.</param>
public sealed record PlaceboWalkResult(IReadOnlyList<PlaceboOutcome> Outcomes);
