using System.Globalization;
using System.Text;

namespace XDrishti.Domain.Learning;

/// <summary>The entry-window proposals of every cell and the honest limits that ride with them (P6-14, P6-00 D5).</summary>
/// <param name="Options">The window knobs used.</param>
/// <param name="Validation">The fold knobs used.</param>
/// <param name="Rows">Rows given.</param>
/// <param name="From">Earliest trade date.</param>
/// <param name="To">Latest trade date.</param>
/// <param name="UsedFolds">Shared folds that took part.</param>
/// <param name="OutsideSession">Rows whose decision minute is outside the regular session (not bucketed).</param>
/// <param name="Cells">One proposal per cell, ordered by cell key.</param>
/// <param name="Limits">Sentences that must be printed with the result.</param>
public sealed record EntryWindowReport(
    EntryWindowOptions Options,
    ValidationOptions Validation,
    int Rows,
    DateOnly From,
    DateOnly To,
    int UsedFolds,
    int OutsideSession,
    IReadOnlyList<EntryWindowProposal> Cells,
    IReadOnlyList<string> Limits)
{
    /// <summary>A fixed plain-text rendering; the same report gives the same text.</summary>
    public string ToText()
    {
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine(string.Create(c, $"entry windows: {Rows} trades, {From:yyyy-MM-dd} to {To:yyyy-MM-dd}, {UsedFolds} folds, buckets of {Options.BucketMinutes} min"));
        foreach (var p in Cells)
        {
            var window = p.WindowStartMinute is { } s && p.WindowEndMinute is { } e ? $"{Clock(s)}-{Clock(e)}" : "none";
            sb.AppendLine(string.Create(c, $"{p.CellKey}: {p.Status}, window {window}, cell n {p.CellTrades}"));
            if (p.ShrunkRate is { } rate && p.CellWinRate is { } cell)
            {
                sb.AppendLine(string.Create(c, $"  window n {p.WindowTrades} ({p.KeptShare:P0} of the cell), shrunk win rate {rate:F3} vs cell {cell:F3}, lift {p.Lift:F3}, mean R inside {Show(p.MeanRInside)} outside {Show(p.MeanROutside)}"));
                sb.AppendLine(string.Create(c, $"  folds: held {p.FoldsHeld} of {p.FoldsEvaluable} evaluable, {p.FoldsRequired} required, pooled out-of-sample lift {(p.PooledLift is { } pl ? pl.ToString("F3", c) : "n/a")}, fold windows overlapping it {p.FoldsOverlapFinal}, equal {p.FoldsEqualFinal}"));
            }

            foreach (var reason in p.Reasons)
            {
                sb.AppendLine("  - " + reason);
            }
        }

        foreach (var limit in Limits)
        {
            sb.AppendLine("limit: " + limit);
        }

        return sb.ToString();
    }

    /// <summary>HH:mm of a minute of the day.</summary>
    public static string Clock(int minute) => string.Create(CultureInfo.InvariantCulture, $"{minute / 60:D2}:{minute % 60:D2}");

    private static string Show(double? v) => v is { } x ? x.ToString("F2", CultureInfo.InvariantCulture) : "n/a";
}
