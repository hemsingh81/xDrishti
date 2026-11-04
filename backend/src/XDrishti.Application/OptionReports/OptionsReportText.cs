using System.Globalization;
using System.Text;
using XDrishti.Domain.Options.Reports;

namespace XDrishti.Application.OptionReports;

/// <summary>The printed form of the options report in the P8 states (spec P14-16): an unavailable figure is the word <c>unavailable</c> with its reason, never a digit; the limits block is always printed last.</summary>
internal static class OptionsReportText
{
    /// <summary>The limits that always apply; printed whatever the data say.</summary>
    public static IReadOnlyList<string> Limits { get; } =
    [
        "read only: counts, shares, dates and verdict words; no price, IV, Greek, bar or quote is shown",
        "unavailable names its reason (not_recorded, no_model, no_runs, no_pairs, no_rejection) and is never a number",
        "expression counts and shares are of the whole stored run, hold-out included; no figure of return is shown here",
        "the archive counts rest on the stored quality results over all checked history (a session is usable when coverage passes and parity passes or warns); the gaps list includes the pass sessions; a session not yet checked is not counted",
        "verdicts are the newest stored record per family; a NOT_EVALUABLE record names what is missing; the basis families are not built and are not listed",
    ];

    public static string Render(OptionsReportData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var text = new StringBuilder();
        text.AppendLine("options and stat-arb report (read only)").AppendLine();
        text.AppendLine("option archive");
        if (data.Archive.Underlyings.Count == 0)
        {
            text.AppendLine("  nothing stored: unavailable (not_recorded)");
        }

        foreach (var u in data.Archive.Underlyings)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"  {Show(u.Underlying)}: recorded {Count(u.RecordedSessions)}, usable {Count(u.UsableSessions)}, first {Date(u.FirstDate)}, last {Date(u.LastDate)}");
            text.AppendLine("    gaps: " + (u.Gaps.State == ReportStates.Ok ? string.Join(", ", u.Gaps.Outcomes.Select(o => o.Outcome + (o.Reason is null ? string.Empty : " (" + o.Reason + ")") + " " + o.Sessions.ToString(CultureInfo.InvariantCulture))) : Unavailable(u.Gaps.Reason)));
            text.AppendLine("    backfill keys: " + (u.Backfill.State == ReportStates.Ok ? string.Create(CultureInfo.InvariantCulture, $"done {u.Backfill.Done}, empty {u.Backfill.Empty}, no data {u.Backfill.NoData}, given up {u.Backfill.GivenUp}") : Unavailable(u.Backfill.Reason)));
            text.AppendLine(CultureInfo.InvariantCulture, $"    spread model: sessions {Count(u.SpreadModelSessions)}, first {Date(u.SpreadModelFirstSession)}, last {Date(u.SpreadModelLastSession)}");
        }

        text.AppendLine().AppendLine("expression runs (newest first)");
        if (data.ExpressionRuns.State != ReportStates.Ok)
        {
            text.AppendLine("  " + Unavailable(data.ExpressionRuns.Reason));
        }

        foreach (var r in data.ExpressionRuns.Items)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"  run {r.Id[..12]} profile {r.ProfileHash[..12]} remark {Show(r.RemarkVersion)}: trades {r.Trades}, paired {r.Paired}, unpaired {(r.Unpaired.Count == 0 ? "none" : string.Join(", ", r.Unpaired.Select(x => Show(x.Reason) + " " + x.Trades.ToString(CultureInfo.InvariantCulture))))}");
            text.AppendLine("    model-price share " + Share(r.FallbackShare) + ", derived-expiry share " + Share(r.ExpiryDerivedShare) + ", recent-archive-spread share " + Share(r.SpreadFromRecentArchiveShare));
            text.AppendLine("    lot feasibility: " + string.Join(", ", r.LotFeasibility.Select(l => Show(l.Feasibility) + " " + Share(l.Share))));
        }

        text.AppendLine().AppendLine("pair formations (oldest quarter first)");
        if (data.PairFormations.State != ReportStates.Ok)
        {
            text.AppendLine("  " + Unavailable(data.PairFormations.Reason));
        }

        foreach (var f in data.PairFormations.Items)
        {
            var h = f.HalfLives;
            text.AppendLine(CultureInfo.InvariantCulture, $"  {Show(f.Quarter)} {Show(f.Family)}: candidates {f.Candidates}, tested {f.Tested}, unavailable {f.Unavailable}, selected {f.Selected}, BH threshold {Share(f.BhThreshold)}");
            text.AppendLine("    half-life (sessions): " + (h.State == ReportStates.Ok ? string.Create(CultureInfo.InvariantCulture, $"n {h.Count}, min {h.Min:F1}, median {h.Median:F1}, max {h.Max:F1}") : Unavailable(h.Reason)));
        }

        text.AppendLine().AppendLine("verdicts (newest stored record per family)");
        foreach (var v in data.Verdicts.Families)
        {
            if (v.State != ReportStates.Stored)
            {
                text.AppendLine("  " + Show(v.Family) + ": " + v.State);
                continue;
            }

            text.AppendLine(CultureInfo.InvariantCulture, $"  {Show(v.Family)}: {Show(v.Verdict ?? string.Empty)}, trials {v.Trials}, folds {v.FoldsEvaluable} of {v.FoldsTotal}, hold-out {(v.HoldOutRead == true ? "read" : "not read")}");
            text.AppendLine("    missing: " + (v.Missing.Count == 0 ? "none" : string.Join("; ", v.Missing.Select(Show))));
            foreach (var limit in v.Limits)
            {
                text.AppendLine("    limit: " + Show(limit));
            }
        }

        text.AppendLine().AppendLine("limits");
        foreach (var limit in data.Limits)
        {
            text.AppendLine("  " + limit);
        }

        return text.ToString();
    }

    private static string Unavailable(string? reason) => "unavailable (" + (reason ?? ReportStates.Reasons.NotRecorded) + ")";

    private static string Count(ReportedCount c) => c.State == ReportStates.Ok ? c.Value!.Value.ToString(CultureInfo.InvariantCulture) : Unavailable(c.Reason);

    private static string Date(ReportedDate d) => d.State == ReportStates.Ok ? d.Value!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : Unavailable(d.Reason);

    private static string Share(ReportedShare s) => s.State == ReportStates.Ok ? s.Value!.Value.ToString("F4", CultureInfo.InvariantCulture) : Unavailable(s.Reason);

    private static string Show(string text) => new([.. text.Take(200).Select(ch => char.IsControl(ch) ? '?' : ch)]);
}
