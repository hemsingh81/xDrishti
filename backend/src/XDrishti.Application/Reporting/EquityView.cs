namespace XDrishti.Application.Reporting;

/// <summary>The equity curves of a report for a lens, one per group, with the evidence of the report. At most <see cref="MaxSeries"/> groups are drawn; a lens with more is refused rather than cut silently.</summary>
/// <param name="Dimensions">The dimensions grouped by.</param>
/// <param name="NonAdditive">A trade is in more than one curve.</param>
/// <param name="Series">The curves.</param>
/// <param name="Evidence">The evidence of the report.</param>
public sealed record EquityView(IReadOnlyList<string> Dimensions, bool NonAdditive, IReadOnlyList<EquitySeriesView> Series, ReportEvidenceView Evidence)
{
    /// <summary>The most curves one response draws.</summary>
    public const int MaxSeries = 40;
}
