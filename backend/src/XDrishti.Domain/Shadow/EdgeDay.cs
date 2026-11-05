namespace XDrishti.Domain.Shadow;

/// <summary>One session's contribution to the edge count.</summary>
/// <param name="Candidates">The candidates of the session that count for edge (<c>m</c> in the design effect).</param>
/// <param name="Graded">How many of them are graded.</param>
public sealed record EdgeDay(int Candidates, int Graded);
