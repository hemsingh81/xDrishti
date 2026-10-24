namespace XDrishti.Application.Reporting;

/// <summary>What stands behind the figures of one group: its trades, the dates they span, whether any contributing run is provisional and which runs they came from.</summary>
/// <param name="N">Trades in the group.</param>
/// <param name="FirstDate">Earliest entry date; null without a trade.</param>
/// <param name="LastDate">Latest exit date; null without a trade.</param>
/// <param name="Provisional">A contributing run is marked provisional.</param>
/// <param name="RunKeys">The runs behind the trades, sorted.</param>
public sealed record GroupEvidenceView(int N, DateOnly? FirstDate, DateOnly? LastDate, bool Provisional, IReadOnlyList<string> RunKeys);
