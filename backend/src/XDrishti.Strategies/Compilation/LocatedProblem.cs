namespace XDrishti.Strategies.Compilation;

/// <summary>
/// A <see cref="Problem"/> of a strategy and where it is: the <paramref name="Path"/> in the model (<c>buy.trigger[1]</c>, <c>buy.stop.lookback</c>, <c>time_rules.last_entry</c>, <c>filters[0]</c>; empty for the problem that
/// says some were dropped). The span of the problem is inside the text of the item at that path (a problem of the language), or (0, 0, line 1, column 1) for a problem of the model, which has no text.
/// </summary>
public sealed record LocatedProblem(string Path, Problem Problem);
