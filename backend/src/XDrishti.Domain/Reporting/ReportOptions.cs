using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Reporting;

/// <summary>
/// The knobs of a report. The prior strength, the credible level and the minimum sample are those of the cell statistics (P6-01, ADR 0064); the recency half-life is not used (a report describes the whole stored history, each trade counts once with weight 1).
/// </summary>
public sealed record ReportOptions
{
    /// <summary>Which R the report is computed on; gross unless the net basis is both requested and confirmed.</summary>
    public ReportBasis Basis { get; init; } = ReportBasis.Gross;

    /// <summary>
    /// The owner's confirmation of the stored charge sheet (ISS-25, P8-00 question 19), printed in the evidence of a net report. Without it a net report is refused: no report is called net on a sheet nobody confirmed. P8-06 must fill it only from the stored
    /// owner confirmation, never from a request field.
    /// </summary>
    public ChargeSheetConfirmation? NetBasisConfirmation { get; init; }

    /// <summary>Wins a cell needs before an expectancy bound is formed from its payoffs (a cell with fewer is not ranked): the mean win of two wins is an outlier, not an estimate.</summary>
    public int MinWins { get; init; } = 5;

    /// <summary>Non-winning trades (R of 0 or less) a cell needs before an expectancy bound is formed.</summary>
    public int MinNonWins { get; init; } = 5;

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

        if (MinWins is < 1 or > 100000 || MinNonWins is < 1 or > 100000)
        {
            problems.Add("min_wins and min_non_wins must be 1 to 100000");
        }

        if (NetBasisConfirmation is { } confirmation && string.IsNullOrWhiteSpace(confirmation.SheetId))
        {
            problems.Add("the charge sheet confirmation needs a sheet id");
        }

        return problems.AsReadOnly();
    }
}
