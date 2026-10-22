using System.Buffers;
using XDrishti.Domain.Research;

namespace XDrishti.SimExport.Csv;

/// <summary>
/// <c>exit-selection.csv</c> (spec P4-06, D9): one row for each fold and candidate, with the dates of the fold, the train and test figures to four decimals, the flags of the fold's winner and, on every row, the in-sample line of the record
/// (<see cref="ExitSelection.InSampleLine"/>), so that a row read alone still says what it is. A run with no fold writes one row, <c>none</c>. UTF-8 without a byte order mark, <c>\n</c> line ends, RFC 4180 quoting, like <see cref="TradesCsv"/>.
/// </summary>
public static class ExitSelectionCsv
{
    /// <summary>The columns, in order.</summary>
    public static IReadOnlyList<string> Columns { get; } = new List<string>
    {
        "fold", "test_start", "test_end", "train_cutoff", "candidate", "unit", "qualified", "train_trades", "train_mean_r", "train_score", "test_trades", "test_score", "fold_winner", "on_plateau", "isolated", "lone_peak", "thin", "note",
    }.AsReadOnly();

    /// <summary>Writes the file.</summary>
    /// <param name="sink">Where the UTF-8 bytes go; the host owns it.</param>
    /// <param name="selection">The record.</param>
    public static void Write(IBufferWriter<byte> sink, ExitSelection selection)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(selection);
        var row = new CsvRow();
        row.Begin();
        foreach (var column in Columns)
        {
            row.Raw(column);
        }

        row.End(sink);
        if (selection.Folds.Count == 0)
        {
            row.Begin();
            row.Raw("none");
            for (var i = 0; i < 4; i++)
            {
                row.Empty();
            }

            row.Raw(selection.Input.Unit);
            for (var i = 0; i < 11; i++)
            {
                row.Empty();
            }

            row.Text(ExitSelection.InSampleLine);
            row.End(sink);
            return;
        }

        foreach (var fold in selection.Folds)
        {
            foreach (var candidate in fold.Rows)
            {
                var winner = string.Equals(candidate.Id, fold.Winner, StringComparison.Ordinal);
                row.Begin();
                row.Number(fold.Index);
                row.Date(fold.TestStart);
                row.Date(fold.TestEnd);
                if (fold.TrainCutoff is { } cutoff)
                {
                    row.Date(cutoff);
                }
                else
                {
                    row.Empty();
                }

                row.Text(candidate.Id);
                row.Raw(selection.Input.Unit);
                row.Bool(candidate.Qualified);
                row.Number(candidate.TrainTrades);
                Fixed(row, candidate.TrainMeanR);
                Fixed(row, candidate.TrainScore);
                row.Number(candidate.TestTrades);
                Fixed(row, candidate.TestScore);
                row.Bool(winner);
                row.Bool(winner && fold.OnPlateau);
                row.Bool(winner && fold.Isolated);
                row.Bool(winner && fold.LonePeak);
                row.Bool(fold.Thin);
                row.Text(ExitSelection.InSampleLine);
                row.End(sink);
            }
        }
    }

    private static void Fixed(CsvRow row, double? value)
    {
        if (value is { } number)
        {
            row.Raw(ComparisonFormat.Fixed(number));
        }
        else
        {
            row.Empty();
        }
    }
}
