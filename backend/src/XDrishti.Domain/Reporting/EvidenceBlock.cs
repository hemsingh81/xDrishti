using XDrishti.Domain.Common;

namespace XDrishti.Domain.Reporting;

/// <summary>What stands behind a figure (P8-00 D1): how many trades, over which dates, on which basis, from which runs, and whether any of them is provisional.</summary>
/// <param name="N">Trades in the group.</param>
/// <param name="FirstDate">The earliest entry date; null without a trade.</param>
/// <param name="LastDate">The latest exit date; null without a trade.</param>
/// <param name="Basis">Gross or net R.</param>
/// <param name="Provisional">At least one contributing run is marked provisional.</param>
/// <param name="RunKeys">The runs behind the trades, sorted.</param>
public sealed record EvidenceBlock(int N, DateOnly? FirstDate, DateOnly? LastDate, ReportBasis Basis, bool Provisional, ValueList<string> RunKeys)
{
    /// <summary>How many runs contributed.</summary>
    public int RunCount => RunKeys.Count;
}
