using System.Globalization;
using XDrishti.Api.Simulate.Assist;
using XDrishti.SimReader;

namespace XDrishti.Api.Simulate.Gate;

/// <summary>
/// The rules of the automated gate (spec P3-09d D2, ADR 0048), pure: the verdicts of the assisted read and the results of the spot checks in, a <see cref="GateDecision"/> out. Every category is computed from the rows on its own, never from the row's verdict (the verdict of the assisted read
/// has a precedence that hides a category behind a stronger one), so one defect never hides a data problem and all the counts are reported. The status is the first of: <c>BLOCKED_DEFECT</c> (the reader on the STORED bars differs from the engine, or reads no trade, or the engine row is absent, or a spot check
/// contradicts the run record, or the pack has no trade); <c>DATA_REPAIR_NEEDED</c> (a gross bar problem other than a hole: a price level that is not constant, a gap above <see cref="BarComparison.GrossGapPercent"/> % of price); <c>RERUN_NEEDED</c> (a row with no second source);
/// <c>PASS_WITH_DATA_FINDINGS</c> (only the reader on the second source differs: the trade depends on the feed); <c>PASS</c>. No clock, no random source: the same inputs give the same decision.
/// </summary>
internal static class GateRules
{
    public static GateDecision Decide(IReadOnlyList<RowVerdict> verdicts, IReadOnlyList<SpotResult> spots, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> engine, IReadOnlyList<string>? integrity = null)
    {
        ArgumentNullException.ThrowIfNull(verdicts);
        ArgumentNullException.ThrowIfNull(spots);
        ArgumentNullException.ThrowIfNull(engine);

        var defects = new List<GateFinding>();
        var repairs = new List<GateFinding>();
        var reruns = new List<GateFinding>();
        var data = new List<GateFinding>();
        if (verdicts.Count == 0)
        {
            defects.Add(new GateFinding(0, string.Empty, string.Empty, default, "the pack has no trade row: nothing was verified"));
        }

        defects.AddRange((integrity ?? []).Select(p => new GateFinding(0, string.Empty, string.Empty, default, "pack integrity: " + p)));
        foreach (var v in verdicts)
        {
            var row = v.Row;
            if (row.Unavailable is not null)
            {
                reruns.Add(Finding(row, row.Unavailable));
                continue;
            }

            if (v.Verdict == Verdicts.CannotRead || v.Fields.Count > 0)
            {
                defects.Add(Finding(row, Repro(v, engine)));
            }

            var gross = row.Bars.Where(b => b.Gross).ToList();
            if (gross.Any(b => !IsHole(b)))
            {
                repairs.Add(Finding(row, string.Join("; ", gross.Where(b => !IsHole(b)).Select(b => b.Minute + " " + b.Words))));
            }

            var parts = new List<string>();
            if (gross.Any(IsHole))
            {
                parts.Add("missing minutes: " + string.Join(", ", gross.Where(IsHole).Select(b => b.Minute + " " + b.Words + Between(row, b.Minute))));
            }

            if (v.Fields.Count == 0 && v.SecondFields.Count > 0)
            {
                parts.Add("the reader on the second source differs from the engine on: " + string.Join(", ", v.SecondFields) + "; " + Gap(row));
            }

            if (parts.Count > 0)
            {
                data.Add(Finding(row, string.Join("; ", parts)));
            }
        }

        foreach (var s in spots.Where(s => s.Outcome == SpotOutcome.Contradicted))
        {
            defects.Add(new GateFinding(s.Line.Seq, s.Line.Key, s.Line.Symbol, s.Line.Date, "spot check contradicted: " + s.Checked));
        }

        var counts = new GateCounts(
            verdicts.Count, defects.Count, repairs.Count, reruns.Count, data.Count,
            spots.Count(s => s.Outcome == SpotOutcome.Confirmed), spots.Count(s => s.Outcome == SpotOutcome.Contradicted), spots.Count(s => s.Outcome == SpotOutcome.NotMachineCheckable));
        var status = defects.Count > 0 ? GateStatus.BlockedDefect
            : repairs.Count > 0 ? GateStatus.DataRepairNeeded
            : reruns.Count > 0 ? GateStatus.RerunNeeded
            : data.Count > 0 ? GateStatus.PassWithDataFindings
            : GateStatus.Pass;
        return new GateDecision(status, counts, defects, repairs, reruns, data, spots, Actions(status, counts, repairs));
    }

    /// <summary>A minute missing on a side: a hole in the data, a data finding and not evidence against the engine (ADR 0048).</summary>
    private static bool IsHole(MinuteComparison b) => b.Words.StartsWith("missing in ", StringComparison.Ordinal);

    private static string Between(AssistRow row, string minute)
    {
        var reading = row.StoredReading ?? row.Reading;
        if (reading is null)
        {
            return " (trade minutes unknown)";
        }

        var entry = AssistLog.Time(reading.EntryEpoch)[11..];
        var exit = AssistLog.Time(reading.ExitEpoch)[11..];
        return string.CompareOrdinal(minute, entry) >= 0 && string.CompareOrdinal(minute, exit) <= 0 ? " (between the entry and exit minutes: YES)" : " (between the entry and exit minutes: no)";
    }

    private static GateFinding Finding(AssistRow row, string detail) => new(row.Pack.Seq, row.Pack.TradeKey, row.Pack.Symbol, row.Pack.Date, detail);

    private static string Gap(AssistRow row)
    {
        var paired = row.Bars.Where(b => b.Percent is not null).ToList();
        return paired.Count == 0
            ? "no paired minute to size the gap"
            : "largest gap " + BarComparison.Plural(paired.Max(b => b.Ticks)) + " and " + paired.Max(b => b.Percent!.Value).ToString("0.00", CultureInfo.InvariantCulture) + " % of price over " + paired.Count.ToString(CultureInfo.InvariantCulture) + " paired minutes";
    }

    /// <summary>The minimal repro of a defect row: the day, the side, the parameters and both sides' values, so that a failing test can be written from it.</summary>
    private static string Repro(RowVerdict v, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> engine)
    {
        var p = v.Row.Pack;
        var head = $"{p.Strategy} or_min={p.OrMinutes.ToString(CultureInfo.InvariantCulture)} rr={p.RewardRisk.ToString(CultureInfo.InvariantCulture)} {p.Side} {p.Symbol} {p.Date:yyyy-MM-dd} on the stored bars; ";
        if (!engine.TryGetValue(p.TradeKey, out var e))
        {
            return head + "the engine row is absent from engine-values.csv";
        }

        var r = v.Row.StoredReading;
        var engineSide = "engine: entry " + e.GetValueOrDefault("entry_ist") + " @ " + e.GetValueOrDefault("entry_price") + ", stop " + e.GetValueOrDefault("stop_price") + ", target " + e.GetValueOrDefault("target_price")
            + ", exit " + e.GetValueOrDefault("exit_ist") + " @ " + e.GetValueOrDefault("exit_price") + " " + e.GetValueOrDefault("exit_reason") + ", r " + e.GetValueOrDefault("r_exact");
        var readerSide = r is null
            ? "reader: " + (v.Row.StoredNote ?? "no trade")
            : "reader: entry " + AssistLog.Time(r.EntryEpoch) + " @ " + AssistLog.Number(r.EntryPrice) + ", stop " + AssistLog.Number(r.Stop) + ", target " + AssistLog.Number(r.Target)
                + ", exit " + AssistLog.Time(r.ExitEpoch) + " @ " + AssistLog.Number(r.ExitPrice) + " " + r.ExitReason.Code() + ", r " + AssistLog.Number(Math.Round(r.SeenR, 4));
        return head + (v.Fields.Count > 0 ? "differs on " + string.Join(", ", v.Fields) + "; " : string.Empty) + readerSide + "; " + engineSide;
    }

    private static List<string> Actions(GateStatus status, GateCounts counts, IReadOnlyList<GateFinding> repairs)
    {
        var days = string.Join(", ", repairs.Select(r => r.Symbol + " " + r.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        List<string> actions = status switch
        {
            GateStatus.BlockedDefect =>
            [
                "For each row listed under defects, write a failing test from its repro first (backend/CLAUDE.md: a bug fix is a failing test first).",
                "Decide from the rule tables (ADR 0037, 0038, 0040, 0043, 0047) whether the engine or the reader departs from them, and fix that one. A spot check that contradicts the run record is a data or run-record question: find out what changed after the run.",
                "Any change of the engine or of the data voids the pack: draw a new pack with a new seed (p3-check-2, ...) and run the gate on it.",
            ],
            GateStatus.DataRepairNeeded =>
            [
                "Run the gap backfill (doc 05 section 6.1) only for days inside Data:Gaps:LookbackSessions (default 30 sessions, at most 250); it cannot reach older days, and a gross problem on an older day has no repair command yet. Days: " + days + ".",
                "Where the gap is in the second source and not in the stored bars, move second-source-bars.csv away (or fix the folder file) so that the day is fetched again.",
                "Re-run the gate: scripts/verify-pack.sh --gate <pack-folder>. A change of the stored bars changes the trades, so if the engine's trades change, draw a new pack.",
            ],
            GateStatus.RerunNeeded =>
            [
                "Reconnect the second source (the Dhan token in the vault, or the network) or put the missing files in the second-source folder (<TICKER>_<yyyy-MM-dd>.csv); the rows and reasons are listed.",
                "Re-run the gate: scripts/verify-pack.sh --gate <pack-folder> [--second-source-folder <dir>]. The days already fetched are kept in second-source-bars.csv.",
            ],
            GateStatus.PassWithDataFindings =>
            [
                "Record each data finding as an open data-quality finding (the rows, fields and missing minutes are listed); they are not P3 blockers. A hole inside the backfill lookback can be closed with the gap backfill; a hole older than the lookback cannot be reached by it and stays open for a later repair command (the stored history is not edited for the gate).",
                "P3 exit: the status counts as passed together with a green scripts/check.sh; an owner's chart check or a third independent source stays optional extra evidence.",
            ],
            _ =>
            [
                "P3 exit: the status counts as passed together with a green scripts/check.sh; an owner's chart check or a third independent source stays optional extra evidence.",
            ],
        };
        if (status != GateStatus.BlockedDefect && counts.SpotsNotCheckable > 0)
        {
            actions.Add("The spot lines listed as not machine-checkable are not covered by the gate; the owner may check them on a chart (optional).");
        }

        if (status is GateStatus.BlockedDefect or GateStatus.DataRepairNeeded or GateStatus.RerunNeeded)
        {
            var open = new List<string>();
            AddCount(open, counts.Defects, "defects");
            AddCount(open, counts.Repairs, "data repairs");
            AddCount(open, counts.Reruns, "rerun rows");
            AddCount(open, counts.DataFindings, "data findings");
            actions.Add("Counts are independent, none is masked by the status: " + string.Join(", ", open.DefaultIfEmpty("none")) + ".");
        }

        return actions;
    }

    private static void AddCount(List<string> open, int count, string label)
    {
        if (count > 0)
        {
            open.Add(count.ToString(CultureInfo.InvariantCulture) + " " + label);
        }
    }
}
