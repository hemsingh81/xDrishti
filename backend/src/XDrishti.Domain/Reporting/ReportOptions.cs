using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Reporting;

/// <summary>
/// The knobs of a report. The prior strength, the credible level and the minimum sample are those of the cell statistics (P6-01, ADR 0064); the recency half-life is not used (a report describes the whole stored history, each trade counts once with weight 1).
/// </summary>
public sealed record ReportOptions
{
    /// <summary>Which R the report is computed on; gross unless the net basis is both requested and confirmed.</summary>
    public ReportBasis Basis { get; init; } = ReportBasis.Gross;

    /// <summary>The owner has confirmed the stored charge sheet as their own (ISS-25, P8-00 question 19). Without it a net report is refused: no report is called net on a sheet nobody confirmed.</summary>
    public bool NetBasisConfirmed { get; init; }

    /// <summary>Prior strength, credible level and the minimum trades for an estimate or a ranking (30).</summary>
    public CellStatsOptions Stats { get; init; } = new();

    /// <summary>The minimum trades per group for an auto-insight (doc 15 section 6: 15).</summary>
    public int InsightMinTrades { get; init; } = 15;

    /// <summary>The problems of the values, empty when they are usable.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>(Stats.Problems());
        if (InsightMinTrades is < 1 or > 100000)
        {
            problems.Add("insight_min_trades must be 1 to 100000");
        }

        return problems.AsReadOnly();
    }
}
