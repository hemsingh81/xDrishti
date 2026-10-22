using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research;
using XDrishti.SimExport.Json;

namespace XDrishti.Api.Simulate;

/// <summary>
/// The exit-choice step of the <c>simulate</c> verb (spec P4-06, D1): turns the trades of the comparison it just ran (the ungated comparison walk, open-at-end trades left out) into the input of <see cref="ExitSelector"/> and renders the record as
/// the text the folder and the store keep. In memory: no reader of the store.
/// </summary>
internal static class SimulateSelect
{
    /// <summary>The offset of India, which has no daylight saving: the exit date of a trade is the India date of its exit minute.</summary>
    private static readonly TimeSpan IndiaOffset = TimeSpan.FromMinutes(330);

    /// <summary>Who the cell is: strategy, timeframe, sides, style (intraday when the comparison was gated, else swing) and the regime, which is <c>*</c> until P6.</summary>
    public static string CellOf(ComparisonRun comparison)
    {
        var record = comparison.Candidates[0].Run.Strategy;
        return record.Id + " " + record.EvaluationTimeframe.ToCode() + " " + SideText(record) + " " + StyleOf(comparison) + " *";
    }

    public static string StyleOf(ComparisonRun comparison) => comparison.Report.GateApplied ? "intraday" : "swing";

    public static string SideText(StrategyRecord record) => string.Join('+', record.Sides.Select(s => s.ToString().ToUpperInvariant()));

    /// <summary>The input of the choice: the closed trades of every candidate in the unit of the report, the sessions of the calendar over the window and the options.</summary>
    public static SelectionInput InputOf(ComparisonRun comparison, TradingCalendar calendar, SelectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        ArgumentNullException.ThrowIfNull(calendar);
        var costed = comparison.Report.Unit == "RNet";
        var trades = new List<SelectionTrade>();
        foreach (var candidate in comparison.Candidates)
        {
            foreach (var runTrade in candidate.Run.Trades)
            {
                var t = runTrade.Trade;
                var exit = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(t.ExitEpochSeconds).ToOffset(IndiaOffset).DateTime);
                trades.Add(new SelectionTrade(candidate.Id, t.TradeDate, exit, costed ? t.Costing!.RNet : t.RFilled));
            }
        }

        var first = comparison.Candidates[0].Run;
        var sessions = new List<DateOnly>();
        for (var d = first.From; d <= first.To; d = d.AddDays(1))
        {
            if (calendar.IsTradingDay(d))
            {
                sessions.Add(d);
            }
        }

        return new SelectionInput(CellOf(comparison), comparison.Report.Unit, !comparison.Report.GateApplied, comparison.EntriesKey, [.. comparison.Candidates.Select(c => c.Exit)], trades, sessions, options);
    }

    /// <summary>The record as <c>exit-selection.json</c> and the SHA-256 of its UTF-8 bytes: the text the store keeps and the file holds are one.</summary>
    public static (string Json, string Sha256) Record(ExitSelection selection, string format)
    {
        var sink = new ArrayBufferWriter<byte>();
        ExitSelectionJson.Write(sink, selection, format);
        return (new UTF8Encoding(false, true).GetString(sink.WrittenSpan), Convert.ToHexStringLower(SHA256.HashData(sink.WrittenSpan)));
    }

    public static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);
}
