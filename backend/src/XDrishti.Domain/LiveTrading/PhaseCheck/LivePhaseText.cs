using System.Globalization;
using System.Text;

namespace XDrishti.Domain.LiveTrading;

/// <summary>The deterministic text of the live phase check (spec P11-06 criterion 10): the same report gives the same bytes. The caveat is in the headline.</summary>
public static class LivePhaseText
{
    public static string Render(LivePhaseReport r)
    {
        ArgumentNullException.ThrowIfNull(r);
        var t = new StringBuilder();
        t.AppendLine(CultureInfo.InvariantCulture, $"Small-live phase check through {r.Through:yyyy-MM-dd}: {r.ExitCriterion}");
        t.AppendLine(CultureInfo.InvariantCulture, $"window {(r.WindowStart is { } ws ? ws.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "none yet")}, {r.ElapsedDays} days, {r.Sessions} sessions");
        foreach (var c in r.Criteria)
        {
            t.AppendLine(CultureInfo.InvariantCulture, $"  {c.Code} {c.Name}: {c.Verdict} - {c.Detail}");
        }

        t.AppendLine(r.Recommendation);
        t.AppendLine("weeks");
        foreach (var w in r.Weeks)
        {
            var outcomes = string.Join(", ", w.Outcomes.Select(o => $"{o.Key} {o.Value}"));
            t.AppendLine(CultureInfo.InvariantCulture, $"  {w.WeekStart:yyyy-MM-dd}: {w.Sessions} sessions, {w.Tickets} tickets ({(outcomes.Length == 0 ? "none" : outcomes)}), {w.RoundTrips} round trips graded ({w.CumulativeRoundTrips} cumulative), step {w.Step}, scale {(w.Scale is { } sc ? sc.ToString("0.000", CultureInfo.InvariantCulture) : "none")}, capture complete {w.CaptureComplete}, halts {w.Halts}, holds {w.Holds}, breaches {w.Breaches}");
        }

        t.AppendLine("printed, not judged");
        foreach (var p in r.Printed)
        {
            t.AppendLine(CultureInfo.InvariantCulture, $"  {p.Label}: {p.Value}");
        }

        t.AppendLine("setup");
        foreach (var item in r.Setup)
        {
            t.AppendLine(CultureInfo.InvariantCulture, $"  {item.Kind} {item.Name}: {(item.On ? "on" : "off")}");
        }

        t.AppendLine("blockers");
        foreach (var b in r.Blockers)
        {
            t.AppendLine("  " + b);
        }

        t.AppendLine("limits");
        foreach (var l in r.Limits)
        {
            t.AppendLine("  " + l);
        }

        return t.ToString();
    }
}
