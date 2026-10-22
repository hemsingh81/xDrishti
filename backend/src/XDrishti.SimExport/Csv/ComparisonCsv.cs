using System.Buffers;
using System.Globalization;
using XDrishti.Domain.Research;

namespace XDrishti.SimExport.Csv;

/// <summary>
/// <c>comparison.csv</c> and <c>diagnostics.csv</c> of a comparison (spec P4-05, D6, D7): one row for each candidate and sample (its own trades, the common set), and one row for each figure of the MAE/MFE table. The figures are printed
/// to a fixed precision, <see cref="ComparisonFormat"/>, which is the precision the hand tables of the tests are compared to. UTF-8 without a byte order mark, <c>\n</c> line ends, RFC 4180 quoting, like <see cref="TradesCsv"/>.
/// </summary>
public static class ComparisonCsv
{
    private static readonly ExitReason[] Reasons = [ExitReason.Stop, ExitReason.Target, ExitReason.SquareOff, ExitReason.EndOfData, ExitReason.MaxHold, ExitReason.EmaExit, ExitReason.TimeStop];

    /// <summary>The columns of <c>comparison.csv</c>, in order.</summary>
    public static IReadOnlyList<string> Columns { get; } = ColumnList();

    /// <summary>The columns of <c>diagnostics.csv</c>.</summary>
    public static IReadOnlyList<string> DiagnosticColumns { get; } = new List<string> { "candidate", "figure", "value", "kind", "count", "of", "censor_level", "censored", "reading", "cell_trades", "below_minimum" }.AsReadOnly();

    private static System.Collections.ObjectModel.ReadOnlyCollection<string> ColumnList()
    {
        var columns = new List<string> { "candidate", "sample", "unit", "trades", "wins", "win_rate", "mean_r", "median_r", "downside_deviation", "total_r", "profit_factor", "average_hold_minutes", "mean_cost_r" };
        foreach (var reason in Reasons)
        {
            columns.Add("share_" + ExportSpellings.Of(reason));
        }

        columns.AddRange(["paired_entries", "paired_mean_difference_r", "not_common_trades", "gated_blocked_entries", "legs", "stop_loosen_ignored", "adds_taken", "adds_missed", "adds_skipped_risk", "adds_skipped_quantity", "exits_time_stop", "exits_ema_exit"]);
        return columns.AsReadOnly();
    }

    /// <summary>Writes <c>comparison.csv</c>.</summary>
    /// <param name="sink">Where the UTF-8 bytes go; the host owns it.</param>
    /// <param name="report">The report.</param>
    public static void Write(IBufferWriter<byte> sink, ComparisonReport report)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(report);
        var row = new CsvRow();
        row.Begin();
        foreach (var column in Columns)
        {
            row.Raw(column);
        }

        row.End(sink);
        foreach (var candidate in report.Rows)
        {
            Sample(row, sink, report, candidate, "own", candidate.Own, includeTail: true);
            Sample(row, sink, report, candidate, "common", candidate.Common, includeTail: false);
        }
    }

    /// <summary>Writes <c>diagnostics.csv</c>.</summary>
    /// <param name="sink">Where the UTF-8 bytes go; the host owns it.</param>
    /// <param name="report">The report.</param>
    public static void WriteDiagnostics(IBufferWriter<byte> sink, ComparisonReport report)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(report);
        var row = new CsvRow();
        row.Begin();
        foreach (var column in DiagnosticColumns)
        {
            row.Raw(column);
        }

        row.End(sink);
        foreach (var cell in report.Diagnostics)
        {
            foreach (var figure in cell.Figures)
            {
                row.Begin();
                row.Text(cell.Id);
                row.Text(figure.Name);
                row.Raw(figure.Value is { } value ? ComparisonFormat.Fixed(value) : string.Empty);
                row.Raw(figure.IsShare ? "share" : "r");
                row.Number(figure.Count);
                row.Number(figure.Of);
                row.Text(figure.Censor);
                row.Bool(figure.Censored);
                row.Text(figure.Reading ?? string.Empty);
                row.Number(cell.Trades);
                row.Bool(cell.BelowMinimum);
                row.End(sink);
            }
        }
    }

    private static void Sample(CsvRow row, IBufferWriter<byte> sink, ComparisonReport report, ComparisonRow candidate, string name, SampleFigures sample, bool includeTail)
    {
        row.Begin();
        row.Text(candidate.Id);
        row.Raw(name);
        row.Raw(report.Unit);
        row.Number(sample.Trades);
        row.Number(sample.Wins);
        Fixed(row, sample.WinRate);
        Fixed(row, sample.MeanR);
        Fixed(row, sample.MedianR);
        Fixed(row, sample.DownsideDeviation);
        Fixed(row, sample.TotalR);
        Fixed(row, sample.ProfitFactor);
        Fixed(row, sample.AverageHoldMinutes);
        Fixed(row, sample.MeanCostR);
        foreach (var reason in Reasons)
        {
            Fixed(row, sample.ExitShares.FirstOrDefault(s => s.Reason == reason)?.Share ?? (sample.Trades == 0 ? null : 0.0));
        }

        if (name == "common" && candidate.Paired is { } paired)
        {
            row.Number(paired.Entries);
            Fixed(row, paired.MeanDifferenceR);
        }
        else
        {
            row.Empty();
            row.Empty();
        }

        if (includeTail)
        {
            row.Number(candidate.NotCommon);
            row.Number(candidate.GatedBlocked);
            foreach (var counter in candidate.Counters)
            {
                row.Number(counter.Value);
            }
        }
        else
        {
            for (var i = 0; i < 2 + candidate.Counters.Count; i++)
            {
                row.Empty();
            }
        }

        row.End(sink);
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

/// <summary>The printed precision of the comparison tables: four decimals, a zero without its sign, the invariant culture.</summary>
public static class ComparisonFormat
{
    /// <summary>The number to four decimals, <c>-0.0000</c> written as <c>0.0000</c>.</summary>
    /// <exception cref="ArgumentException">The number is not finite.</exception>
    public static string Fixed(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentException("A NaN or an infinity cannot be printed.", nameof(value));
        }

        var text = value.ToString("0.0000", CultureInfo.InvariantCulture);
        return text == "-0.0000" ? "0.0000" : text;
    }

    /// <summary>A share (0 to 1) as a percentage to one decimal, <c>33.3%</c>.</summary>
    public static string Percent(double share) => (share * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";
}
