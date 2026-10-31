using System.Globalization;
using System.Text;

namespace XDrishti.Domain.Execution.PhaseCheck;

/// <summary>The deterministic text of the report (spec P12-12 criterion 12): the same report gives the same bytes; every reason stays on one line; the bare exit word stands next to the labelled criterion.</summary>
public static class ExecutionPhaseText
{
    public static string Render(ExecutionPhaseReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var sb = new StringBuilder();
        Line(sb, string.Create(CultureInfo.InvariantCulture, $"P12 phase check through {ExecutionPhaseCheck.Date(report.Through)}"));
        Line(sb, "exit verdict: " + report.ExitVerdict);
        Line(sb, "exit criterion: " + report.ExitCriterion);
        foreach (var account in report.Accounts)
        {
            Line(sb, string.Empty);
            Line(sb, $"account {ExecutionPhaseCheck.OneLine(account.Name)} ({account.AccountId})");
            Line(sb, $"  verdict: {account.Label}");
            Line(sb, "  mode: " + ExecutionPhaseCheck.OneLine(account.Mode) + "; window start: " + (account.WindowStart is { } w ? ExecutionPhaseCheck.Date(w) : "none"));
            Line(sb, string.Create(CultureInfo.InvariantCulture, $"  streak {account.Streak} of {report.Needed}; counted {account.Counted}, idle {account.Idle}, broken {account.Broken}, out of mode {account.OutOfMode}, sandbox {account.SandboxSessions}"));
            if (account.FirstMetAt is { } first)
            {
                Line(sb, "  first met on " + ExecutionPhaseCheck.Date(first));
            }

            if (account.LastBreak is { } lastBreak)
            {
                Line(sb, "  last break: " + lastBreak);
            }

            foreach (var s in account.Sessions)
            {
                var median = s.MedianMinutes is { } m ? m.ToString("0.0", CultureInfo.InvariantCulture) + " min" : "insufficient";
                Line(
                    sb,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"  {ExecutionPhaseCheck.Date(s.Date)} {s.Class}: orders {s.OrdersSent}, sandbox {s.SandboxIntents}, tickets {s.Tickets.Issued}/{s.Tickets.Confirmed}/{s.Tickets.Skipped}/{s.Tickets.Expired}/{s.Tickets.Refused}, eod {s.EodRun}, final {s.FinalRun}, breaks {s.Breaks}{Kinds(s.BreakKinds)}, alarms {Kinds(s.Alarms, "none")}, halts {Kinds(s.Halts, "none")}, day state {s.DayState}, tap {median}{Reasons(s.Reasons)}"));
            }

            foreach (var fact in account.Facts)
            {
                Line(sb, $"  fact {fact.Name}: {fact.State}, {fact.Text}");
            }
        }

        Line(sb, string.Empty);
        Line(sb, $"boundary: {report.Boundary.Text}");
        Line(sb, "honest limits:");
        foreach (var limit in report.Limits)
        {
            Line(sb, "  - " + limit);
        }

        return sb.ToString();
    }

    private static string Kinds(IReadOnlyList<string> kinds) => kinds.Count == 0 ? string.Empty : " (" + string.Join(", ", kinds) + ")";

    private static string Kinds(IReadOnlyList<string> kinds, string none) => kinds.Count == 0 ? none : string.Join("+", kinds);

    private static string Reasons(IReadOnlyList<string> reasons) => reasons.Count == 0 ? string.Empty : "; reasons " + string.Join(", ", reasons);

    private static void Line(StringBuilder sb, string text) => sb.Append(text).Append('\n');
}
