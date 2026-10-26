using System.Globalization;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Paper;
using XDrishti.Domain.Reporting;

namespace XDrishti.Application.Paper;

/// <summary>Why the band cannot be drawn: the state of its figures and the reason.</summary>
/// <param name="State">insufficient, unavailable or not_evaluable.</param>
/// <param name="Reason">The words.</param>
internal sealed record BandGate(string State, string Reason);

/// <summary>The cells of the counted set against their references (spec P10-04, criterion 6).</summary>
/// <param name="Views">One row per paper cell.</param>
/// <param name="References">The references of the paper cells that have a usable one.</param>
/// <param name="Gate">Why the band is not drawn, or null.</param>
internal sealed record CellAnalysis(IReadOnlyList<CompareCellView> Views, IReadOnlyList<CellReference> References, BandGate? Gate);

/// <summary>Builds the per-cell rows and decides whether the band may be drawn: a cell never silently drops out.</summary>
internal static class CompareCells
{
    /// <summary>The fewest trades a cell reference (and a cell's own paper mean) needs.</summary>
    public const int MinCellTrades = PaperExitStatistics.ProvisionalFrom;

    public static CellAnalysis Analyze(IReadOnlyList<CountedTrade> trades, ReferenceRead reference)
    {
        if (reference.Problem is not null)
        {
            // The reference was not read or is missing: say so on every cell, never "no reference" for a cell nobody looked up.
            var state = reference.NotRead ? "not_read" : "unavailable";
            var views0 = trades.GroupBy(t => t.CellKey, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => new CompareCellView(
                g.Key, g.Count(),
                g.Count() >= MinCellTrades ? ReportViews.Metric(Metric.Of(g.Average(t => t.R))) : ReportViews.Metric(Metric.Insufficient(string.Create(CultureInfo.InvariantCulture, $"{g.Count()} paper trades, {MinCellTrades} are needed"))),
                0, ReportViews.Metric(Metric.Unavailable(reference.Problem)), state, reference.Problem)).ToList();
            return new CellAnalysis(views0, [], null);
        }

        var views = new List<CompareCellView>();
        var references = new List<CellReference>();
        var differs = new List<string>();
        var thin = new List<string>();
        foreach (var group in trades.GroupBy(t => t.CellKey, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var paper = group.ToList();
            var paperMean = paper.Count >= MinCellTrades
                ? ReportViews.Metric(Metric.Of(paper.Average(t => t.R)))
                : ReportViews.Metric(Metric.Insufficient(string.Create(CultureInfo.InvariantCulture, $"{paper.Count} paper trades, {MinCellTrades} are needed")));
            if (!reference.Cells.TryGetValue(group.Key, out var cell) || cell.Results.Count == 0)
            {
                thin.Add(group.Key);
                views.Add(new CompareCellView(group.Key, paper.Count, paperMean, 0, ReportViews.Metric(Metric.Unavailable("no backtest trade of this cell before the start")), "no_reference", "no backtest trade of this cell before the start"));
                continue;
            }

            var referenceMean = cell.Results.Count >= MinCellTrades
                ? ReportViews.Metric(Metric.Of(cell.Results.Average()))
                : ReportViews.Metric(Metric.Insufficient(string.Create(CultureInfo.InvariantCulture, $"{cell.Results.Count} backtest trades, {MinCellTrades} are needed")));
            var mismatch = Mismatch(paper, cell);
            if (mismatch is not null)
            {
                differs.Add(group.Key);
                views.Add(new CompareCellView(group.Key, paper.Count, paperMean, cell.Results.Count, referenceMean, "basis_differs", mismatch));
            }
            else if (cell.Results.Count < MinCellTrades)
            {
                thin.Add(group.Key);
                views.Add(new CompareCellView(group.Key, paper.Count, paperMean, cell.Results.Count, referenceMean, "thin_reference", string.Create(CultureInfo.InvariantCulture, $"{cell.Results.Count} backtest trades, {MinCellTrades} are needed")));
            }
            else
            {
                references.Add(new CellReference(group.Key, cell.Results));
                views.Add(new CompareCellView(group.Key, paper.Count, paperMean, cell.Results.Count, referenceMean, "ok", null));
            }
        }

        BandGate? gate = null;
        if (differs.Count > 0)
        {
            gate = new BandGate("not_evaluable", "basis differs in " + string.Join(", ", differs));
        }
        else if (thin.Count > 0)
        {
            gate = new BandGate("insufficient", string.Create(CultureInfo.InvariantCulture, $"no usable backtest reference (at least {MinCellTrades} trades before the start) for ") + string.Join(", ", thin));
        }

        return new CellAnalysis(views, references, gate);
    }

    private static string? Mismatch(List<CountedTrade> paper, ReferenceCell cell)
    {
        var wanted = cell.Costed ? "net" : "gross";
        if (paper.Any(t => !string.Equals(t.RBasis, wanted, StringComparison.Ordinal)))
        {
            return $"the backtest is {wanted} of costs but the paper trades are not all {wanted}";
        }

        if (cell.SheetId is { } sheet && paper.Any(t => t.ChargeSheetId is { } id && !string.Equals(id, sheet, StringComparison.Ordinal)))
        {
            return $"the backtest used charge sheet {sheet} but a paper trade used another";
        }

        return null;
    }
}
