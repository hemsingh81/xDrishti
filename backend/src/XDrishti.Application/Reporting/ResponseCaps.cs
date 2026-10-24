namespace XDrishti.Application.Reporting;

/// <summary>
/// How much of a report one answer may hold (P8-06, non-functional): a lens of three dimensions over a few hundred instruments can name tens of thousands of groups. The engine takes the cap as an argument and stops its pass over the trades at the first group past it, before
/// any figure, point, bin or cell is built, and refuses with 422 <c>reports.invalid</c> naming the cap; it never cuts an answer silently (a cut answer would read as the whole). What is bounded is the grouping, the figures and the answer; the read of the trades and the
/// pass that finds the groups are bounded by <see cref="ReportLoader.MaxClosedTrades"/>.
/// </summary>
internal static class ResponseCaps
{
    /// <summary>The most groups of one answer.</summary>
    public const int Groups = 2000;

    /// <summary>The most cells of one pivot (rows times columns).</summary>
    public const int PivotCells = 10000;

    /// <summary>The most distributions of one answer (each holds 22 bins).</summary>
    public const int DistributionGroups = 200;

    /// <summary>The most cells of one "what works" answer.</summary>
    public const int Cells = 2000;
}
