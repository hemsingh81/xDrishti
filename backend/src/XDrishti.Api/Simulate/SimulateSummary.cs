using System.Data.Common;
using System.Globalization;
using System.Text;
using XDrishti.Domain.Research;

namespace XDrishti.Api.Simulate;

/// <summary>
/// The summary the verb prints after the banner (spec P3-04b2, D6): the run key and the folder, the database that was reached (host, port and name only), the instruments run and skipped by reason, the candidates, the trades, the trades the
/// data cut short and the entries a flagged day suppressed, the ticks table, the notes of the tick check, the leftover staging folders and the phase times. It prints no statistic: no split of the trades by exit reason or side, no R value, no
/// percentage. The words that would read as a performance statistic are kept out of every line (a test scans for them).
/// </summary>
internal static class SimulateSummary
{
    /// <summary>The most skipped instruments listed one per line.</summary>
    public const int MaxSkippedListed = 50;

    public static string Text(SimulationRun run, ExportOutcome outcome, SimulateUniverse universe, SimulatePhases phases, int cards, int cap, string? connectionString)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(universe);
        ArgumentNullException.ThrowIfNull(phases);

        var text = new StringBuilder();
        text.Append('\n');
        Line(text, "run key: " + run.RunKey);
        Line(text, "folder: " + outcome.Folder);
        Line(text, "export: " + Status(outcome));
        Line(text, "database: " + Database(connectionString));
        Instruments(text, run);
        Line(text, "candidates: " + Number(run.Totals.CandidatesInWindow));
        Line(text, "trades: " + Number(run.Trades.Count));
        Line(text, "open at end (the data cut them short): " + Number(run.OpenAtEnd.Count));
        Line(text, cap == 0 ? "trade cards: none (--cards 0)" : "trade cards: " + Number(cards) + " written (up to " + Number(cap) + " asked for), with index.html");
        Line(text, "entries a flagged day suppressed: " + Number(run.Totals.EntriesSuppressed));
        Ticks(text, run);
        Notes(text, universe, outcome);
        Line(text, Phases(run, phases));
        return text.ToString();
    }

    /// <summary>The host, port and name of the connection string; never the user or the password (the password is not in the string).</summary>
    public static string Database(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return "(not configured)";
        }

        try
        {
            var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
            var host = builder.TryGetValue("Host", out var h) ? h?.ToString() : null;
            var port = builder.TryGetValue("Port", out var p) ? p?.ToString() : "5432";
            var name = builder.TryGetValue("Database", out var d) ? d?.ToString() : null;
            return (host ?? "?") + ":" + (port ?? "5432") + "/" + (name ?? "?");
        }
        catch (ArgumentException)
        {
            return "(unreadable)";
        }
    }

    private static string Status(ExportOutcome outcome) => outcome.Status switch
    {
        ExportStatus.Written => "written",
        ExportStatus.Unchanged => "unchanged (the identical export was already there and every file was verified)",
        _ => "replaced (the old export is kept in " + outcome.PreviousFolder + ")",
    };

    private static void Instruments(StringBuilder text, SimulationRun run)
    {
        var skipped = run.Instruments.Where(i => i.Skipped is not null).ToList();
        Line(text, "instruments: " + Number(run.Instruments.Count - skipped.Count) + " run, " + Number(skipped.Count) + " skipped");
        if (skipped.Count == 0)
        {
            return;
        }

        Line(text, "skipped by reason: " + string.Join(", ", run.Totals.Skipped.Select(s => s.Reason.ToCode() + " " + Number(s.Instruments))));
        foreach (var instrument in skipped.Take(MaxSkippedListed))
        {
            Line(text, "  skipped: " + Safe(instrument.Symbol) + " (" + instrument.Skipped!.Value.ToCode() + ")");
        }

        if (skipped.Count > MaxSkippedListed)
        {
            Line(text, "  and " + Number(skipped.Count - MaxSkippedListed) + " more skipped (all are in run.json)");
        }
    }

    /// <summary>The distinct converted ticks the replay used, each with the number of instruments and one example symbol; the instruments that fell back to the NSE default are counted apart.</summary>
    private static void Ticks(StringBuilder text, SimulationRun run)
    {
        var ran = run.Instruments.Where(i => i.Skipped is null && i.TickSize is not null).ToList();
        var fallback = ran.Count(i => i.TickFromFallback);
        var groups = ran.Where(i => !i.TickFromFallback).GroupBy(i => i.TickSize!.Value).OrderBy(g => g.Key).ToList();
        if (groups.Count == 0 && fallback == 0)
        {
            return;
        }

        Line(text, "ticks used (rupees, converted from the master's paise):");
        foreach (var group in groups)
        {
            Line(text, "  " + Rupees(group.Key) + ": " + Number(group.Count()) + " instruments, for example " + Safe(group.First().Symbol));
        }

        if (fallback > 0)
        {
            Line(text, "  NSE default (no tick in the master): " + Number(fallback) + " instruments");
        }
    }

    private static void Notes(StringBuilder text, SimulateUniverse universe, ExportOutcome outcome)
    {
        if (universe.NotChecked.Count > 0)
        {
            Line(text, "not tick-checked (not a share or an ETF): " + string.Join(", ", universe.NotChecked.OrderBy(n => n.Key, StringComparer.Ordinal).Select(n => n.Key + " " + Number(n.Value))));
        }

        if (universe.Retired.Count > 0)
        {
            Line(text, "retired instruments run: " + string.Join(", ", universe.Retired.Select(Safe)));
        }

        if (universe.Duplicates > 0)
        {
            Line(text, "duplicates collapsed: " + Number(universe.Duplicates));
        }

        if (outcome.StaleStaging.Count > 0)
        {
            Line(text, "leftover staging folders of a killed run (remove by hand): " + string.Join(", ", outcome.StaleStaging));
        }
    }

    private static string Phases(SimulationRun run, SimulatePhases phases)
    {
        var seconds = phases.Run.TotalSeconds;
        var speed = seconds > 0 ? (run.Instruments.Count / seconds).ToString("0.0", CultureInfo.InvariantCulture) : "n/a";
        return "phases: resolve and gate " + Ms(phases.ResolveAndGate) + ", run " + Ms(phases.Run)
            + " (validate, prepare, read, replay and assembly: the split is in the finish line of the log), render and write " + Ms(phases.RenderAndWrite)
            + "; " + speed + " instruments per second in the run";
    }

    /// <summary>A tick in rupees without the trailing zeros of the stored scale (0.05, not 0.050000).</summary>
    internal static string Rupees(decimal value) => value.ToString("0.##########", CultureInfo.InvariantCulture);

    private static string Ms(TimeSpan span) => ((long)span.TotalMilliseconds).ToString(CultureInfo.InvariantCulture) + " ms";

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Safe(string text) => SimulateOptions.Show(text);

    private static void Line(StringBuilder text, string line) => text.Append(line).Append('\n');
}
