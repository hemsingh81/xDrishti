namespace XDrishti.Application.Learning;

/// <summary>A stored run whose closed trades make cells, with the labels the run does not carry itself: the style and the exit policy (the verb names them, as for an exit choice).</summary>
/// <param name="RunKey">The 64-hex key of a stored run.</param>
/// <param name="Style"><c>intraday</c> or <c>swing</c>.</param>
/// <param name="Exit">The label of the exit policy the run used.</param>
public sealed record CellSource(string RunKey, string Style, string Exit);
