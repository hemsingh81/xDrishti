using System.Globalization;
using System.Text;

namespace XDrishti.Domain.Learning;

/// <summary>The drift events of a replay over stored history, the transitions they would suggest, and the limits (P6-09). Read only: nothing is recorded and no state is changed.</summary>
/// <param name="Options">The knobs used.</param>
/// <param name="MonitorFromEpochSeconds">The start of the monitored period; what is known before it is the reference.</param>
/// <param name="Cells">One entry per cell, ordered by key.</param>
/// <param name="Calibration">The calibration monitor of a model, or null when none was asked for.</param>
/// <param name="Proposals">Suggested transitions, by time then cell.</param>
/// <param name="Limits">Sentences that must be printed with the result.</param>
public sealed record DriftReport(DriftOptions Options, long MonitorFromEpochSeconds, IReadOnlyList<CellDrift> Cells, CalibrationDrift? Calibration, IReadOnlyList<DriftProposal> Proposals, IReadOnlyList<string> Limits)
{
    /// <summary>A fixed plain-text rendering; the same report gives the same text.</summary>
    public string ToText()
    {
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine(string.Create(c, $"drift replay: monitoring from {Stamp(MonitorFromEpochSeconds)}, {Cells.Count} cells; window {Options.WindowTrades} trades, {Options.BelowBandChecks} checks below the band, checks every {Options.CheckEveryTrades} trades"));
        foreach (var cell in Cells)
        {
            if (cell.Band is not { } b)
            {
                sb.AppendLine(string.Create(c, $"{cell.CellKey}: not monitored ({cell.NotMonitored}); {cell.ReferenceTrades} reference, {cell.MonitoredTrades} monitored trades"));
                continue;
            }

            sb.AppendLine(string.Create(c, $"{cell.CellKey}: {cell.ReferenceTrades} reference, {cell.MonitoredTrades} monitored trades; band edge {b.Lower:F3} R, reference mean {b.Mean:F3} R"));
            AppendEvents(sb, cell.Events);
        }

        if (Calibration is { } cal)
        {
            sb.AppendLine(string.Create(c, $"{cal.Subject}: {cal.Outcomes} outcomes read. {cal.Note}"));
            AppendEvents(sb, cal.Events);
        }

        foreach (var p in Proposals)
        {
            sb.AppendLine(string.Create(c, $"proposed (not recorded) {Stamp(p.AtEpochSeconds)} {p.CellKey}: {(p.Pause ? "pause" : "recovered in shadow, could go back to paper")}: {p.Reason}"));
        }

        foreach (var limit in Limits)
        {
            sb.AppendLine("limit: " + limit);
        }

        return sb.ToString();
    }

    private static void AppendEvents(StringBuilder sb, IReadOnlyList<DriftEvent> events)
    {
        foreach (var e in events)
        {
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  {Stamp(e.AtEpochSeconds)} {e.Detector} {(e.Fired ? "FIRED" : "cleared")}: {e.Evidence}"));
        }
    }

    private static string Stamp(long epochSeconds) => DateTimeOffset.FromUnixTimeSeconds(epochSeconds).ToOffset(TimeSpan.FromMinutes(330)).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}
