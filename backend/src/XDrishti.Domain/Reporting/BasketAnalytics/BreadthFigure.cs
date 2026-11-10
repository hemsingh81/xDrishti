namespace XDrishti.Domain.Reporting;

/// <summary>One breadth share with the members behind it (BK-06 m9).</summary>
/// <param name="Share">The share (0 to 1), or why there is none.</param>
/// <param name="Voters">Members the share rests on.</param>
/// <param name="Excluded">Members left out of this figure, in symbol order.</param>
/// <param name="ExcludedReason">Why they were left out.</param>
public sealed record BreadthFigure(Metric Share, int Voters, IReadOnlyList<string> Excluded, string ExcludedReason);
