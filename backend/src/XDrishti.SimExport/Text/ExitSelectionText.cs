using System.Globalization;
using System.Text;
using XDrishti.Domain.Research;
using XDrishti.SimExport.Csv;

namespace XDrishti.SimExport.Text;

/// <summary>The text of an exit choice for the console and the readme (spec P4-06, D8, D9): the banner lines, the inputs, the folds with their winners, the final choice, the pooled out-of-sample figures and the verdict.</summary>
public static class ExitSelectionText
{
    /// <summary>The summary.</summary>
    /// <param name="selection">The record.</param>
    public static string Summary(ExitSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        var o = selection.Input.Options;
        var text = new StringBuilder();
        text.Append("exit choice for ").Append(selection.Input.Cell).Append(" (unit: ").Append(selection.Input.Unit).Append(")\n");
        foreach (var line in selection.Banner)
        {
            text.Append(line).Append('\n');
        }

        text.Append("walk-forward: expanding train, first test window at least ").Append(Int(o.MinTrainMonths)).Append(" months in, ").Append(Int(o.TestMonths)).Append("-month test windows, embargo ").Append(Int(o.EmbargoSessions))
            .Append(" sessions; score = mean R - ").Append(ComparisonFormat.Fixed(o.Lambda)).Append(" x downside deviation; at least ").Append(Int(o.MinTrades)).Append(" trades; plateau preferred: ").Append(o.PreferPlateau ? "yes" : "no").Append('\n');
        text.Append("folds: ").Append(Int(selection.Folds.Count)).Append(", short final windows dropped: ").Append(Int(selection.DroppedWindows)).Append('\n');
        foreach (var fold in selection.Folds)
        {
            text.Append("  fold ").Append(Int(fold.Index)).Append(' ').Append(fold.TestStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append("..").Append(fold.TestEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                .Append(": train ").Append(Int(fold.TrainTrades)).Append(", test ").Append(Int(fold.TestTrades)).Append(", embargoed ").Append(Int(fold.Embargoed)).Append(", purged ").Append(Int(fold.Purged)).Append("; ");
            if (fold.Winner is null)
            {
                text.Append("no choice\n");
                continue;
            }

            text.Append("winner ").Append(fold.Winner).Append(fold.OnPlateau ? " (plateau)" : fold.LonePeak ? " (lone peak)" : string.Empty).Append(fold.Isolated ? " (no neighbours)" : string.Empty)
                .Append(", train score ").Append(Score(fold.WinnerTrainScore)).Append(", test score ").Append(Score(fold.WinnerTestScore)).Append(" on ").Append(Int(fold.WinnerTestTrades)).Append(" trades").Append(fold.Thin ? " (thin, out of the fraction)" : string.Empty).Append('\n');
        }

        text.Append("final choice: ").Append(selection.FinalCandidate is null ? "no choice, default " + selection.Default : selection.FinalCandidate + " (in-sample" + (selection.FinalPlateau ? ", plateau" : selection.FinalLonePeak ? ", lone peak" : string.Empty) + ")").Append('\n');
        text.Append("pooled out-of-sample, test windows of the folds with a winner\n");
        foreach (var figure in selection.Pooled)
        {
            text.Append("  ").Append(figure.Name).Append(": ").Append(Int(figure.Trades)).Append(" trades, mean R ").Append(Score(figure.MeanR)).Append(", score ").Append(Score(figure.Score)).Append('\n');
        }

        text.Append("verdict: ").Append(selection.Stability.Reason).Append(" (lone peaks: ").Append(Int(selection.Stability.LonePeaks)).Append(")\n");
        return text.ToString();
    }

    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Score(double? value) => value is { } v ? ComparisonFormat.Fixed(v) : "n/a";
}
