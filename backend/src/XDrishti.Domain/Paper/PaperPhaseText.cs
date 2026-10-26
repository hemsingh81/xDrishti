using System.Globalization;

namespace XDrishti.Domain.Paper;

/// <summary>The printable text of the paper phase check (spec P10-06, criterion 9): deterministic, limits included.</summary>
public static class PaperPhaseText
{
    /// <summary>Renders the report; the same report gives the same text.</summary>
    /// <param name="report">The report.</param>
    public static string Render(PaperPhaseReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var c = CultureInfo.InvariantCulture;
        var lines = new List<string>
        {
            "phase check P10 (exit: 60 counted paper trades, positive expectancy after costs within the backtest band, drawdown within the 90th percentile, 12 weeks; ADR 0123; paper only, stored records only, nothing is written)",
            report.Epoch is { } e
                ? string.Create(c, $"epoch {e.Id} ({e.Kind}) started {e.StartedOn:yyyy-MM-dd}: {report.ElapsedDays} elapsed days to {report.Through:yyyy-MM-dd}")
                : string.Create(c, $"no paper epoch yet (through {report.Through:yyyy-MM-dd})"),
            "exit criterion: " + report.ExitCriterion,
        };
        foreach (var criterion in report.Criteria)
        {
            lines.Add($"{criterion.Id}. {criterion.Verdict}  {criterion.Title}");
            lines.Add("   " + criterion.Label);
            lines.AddRange(criterion.Facts.Select(f => "   fact: " + f));
            lines.AddRange(criterion.Blockers.Select(b => "   blocker: " + b));
        }

        lines.Add("epochs (only the current one is judged; earlier ones are never pooled):");
        lines.AddRange(report.Epochs.Select(x => string.Create(c, $"   epoch {x.Id} {x.Kind} from {x.StartedOn:yyyy-MM-dd}: counted {x.Counted}, unexplained changes {x.UnexplainedChanges}{(x.Current ? " (current)" : string.Empty)}")));
        lines.Add("weeks:");
        lines.AddRange(report.Weeks.Select(w => string.Create(
            c, $"   {w.Week}: sessions {w.Sessions}, plans {w.PlansPublished}, failed-closed {w.FailedClosed}, manual {w.ManualRuns}, capture finished {w.CaptureFinished}, counted {w.TradesCounted}, cumulative {w.CumulativeCounted}, cumulative R {w.CumulativeR:+0.00;-0.00;0.00}, frozen changes {w.FrozenChanges}, risk-down changes {w.RiskDownChanges}")));
        var p = report.Printed;
        lines.AddRange(p.DisciplineLines.Select(d => "discipline: " + d));
        lines.Add("sensitivity (all primary proposals, plan prices): " + p.SensitivityMean);
        lines.Add(string.Create(c, $"approval split of counted trades: owner {p.OwnerApproved}, auto {p.AutoApproved}; plans with zero primary tickets: {p.PlansWithoutPrimary}; manual nightly runs: {p.ManualRuns}"));
        lines.Add("P7 phase check: " + p.P7);
        lines.Add("charge sheet confirmed by the owner: " + (p.ChargeSheetConfirmed ? "yes" : "no"));
        lines.AddRange(p.Services.Select(s => $"service {s.Id}: {s.State} ({s.Role}{(s.Required ? ", needed" : string.Empty)})"));
        lines.AddRange(p.Switches.Select(s => $"setting {s.Name}: {(s.On ? "ON" : "OFF")} ({s.Effect})"));
        lines.AddRange(p.Blockers.Select(b => "blocker: " + b));
        lines.AddRange(p.NotJudged.Select(n => $"not judged here: {n.Name}: {n.Verdict}; {n.Proof}"));
        lines.AddRange(report.Limits.Select(l => "limit: " + l));
        return string.Join('\n', lines) + "\n";
    }
}
