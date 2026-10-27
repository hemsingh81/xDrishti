using System.Globalization;
using XDrishti.Application.Planning;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Assistant;
using XDrishti.Domain.Assistant.Text;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Ai.Briefing;

/// <summary>
/// Turns already-read engine outputs into <see cref="BriefingFacts"/> (spec P9-06 AC 1, D1, D6). No I/O. Only system-produced strings and numbers enter; each string is cleaned as untrusted text and cut. Account names and per-account quantities enter only with
/// the portfolio context switch on (and then as one total per ticket). The order is fixed, so the same inputs give the same facts.
/// </summary>
internal sealed class BriefingFactsBuilder
{
    private const int MaxLine = 300;
    private const int MaxRisks = 6;
    private const int MaxDigestLinesPerSection = 5;

    private int _flags;

    /// <summary>Instruction-like patterns found in the strings that were cleaned (counted, the text is still used).</summary>
    public int InjectionFlags => _flags;

    public BriefingFacts Daily(PlanView plan, IReadOnlyList<PlanItemView> items, bool portfolioContext)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(items);
        var tickets = items.Where(i => i.Group != "rejected").OrderBy(i => i.Group == "primary" ? 0 : 1).ThenBy(i => i.Rank).ToList();
        var lines = new List<FactLine>
        {
            new(string.Empty, "plan", "plan", Format($"Plan for {plan.ForDate:yyyy-MM-dd} is {plan.State} with {plan.Primary} primary and {plan.Reserve} reserve tickets; {plan.Rejected} candidates were rejected.")),
        };
        if (tickets.Count == 0)
        {
            lines.Add(new FactLine(string.Empty, "plan", "tickets", "No ticket is planned for this date.", FactLine.Ok));
        }

        if (plan.Provisional)
        {
            lines.Add(new FactLine(string.Empty, "plan", "provisional", "The plan is provisional: its cells are not paper-proven.", FactLine.Ok));
        }

        foreach (var ticket in tickets)
        {
            var symbol = Clean(ticket.Symbol, 40);
            lines.Add(new FactLine(symbol, "ticket", "ticket", $"{symbol} {Clean(ticket.Side, 8)} {ticket.Group} ticket, strategy {Clean(ticket.StrategyId, 60)}."));
            lines.Add(new FactLine(symbol, "ticketdetail", "levels", Levels(symbol, ticket)));
            lines.Add(Evidence(symbol, ticket));
            if (portfolioContext && ticket.Allocations.Count > 0)
            {
                lines.Add(new FactLine(symbol, "ticketdetail", "size", Format($"{symbol} total quantity {ticket.Allocations.Sum(a => a.Quantity)} across accounts.")));
            }
        }

        var risks = new List<string>();
        if (plan.Flags is { } flags)
        {
            risks.AddRange(flags.Banner.Select(b => Clean(b, MaxLine)));
        }

        risks.Add("Basis: " + Clean(plan.Basis, MaxLine));
        risks.Add("Limits: " + Clean(plan.Limits, MaxLine));
        foreach (var risk in risks.Where(r => r.Length > 0).Take(MaxRisks))
        {
            lines.Add(new FactLine(string.Empty, "risk", "risk", risk.EndsWith('.') ? risk : risk + "."));
        }

        lines.Add(new FactLine(string.Empty, "note", "regime", "Regime is unavailable: market context is not stored.", FactLine.Unavailable));
        return new BriefingFacts(BriefingFacts.Daily, plan.ForDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), plan.Provisional, [.. tickets.Select(t => Clean(t.Symbol, 40))], lines, plan.Id, null, []);
    }

    public BriefingFacts Weekly(string week, long digestId, IReadOnlyList<DigestSection> sections, InsightsView? insights, int maxChars)
    {
        ArgumentNullException.ThrowIfNull(sections);
        var lines = new List<FactLine> { new(string.Empty, "plan", "review", $"Weekly review for {week}, built from the stored digest and the report insights.") };
        foreach (var line in Take(sections.FirstOrDefault(s => s.Key == "plans"), MaxDigestLinesPerSection))
        {
            lines.Add(new FactLine(string.Empty, "plan", "plan outcomes", Sentence(line)));
        }

        if (insights is null || insights.Insights.Count == 0)
        {
            lines.Add(new FactLine(string.Empty, "insight", "insights", "Report insights are unavailable.", FactLine.Unavailable));
        }
        else
        {
            foreach (var insight in insights.Insights)
            {
                lines.Add(new FactLine(string.Empty, "insight", Clean(insight.Kind, 40).Replace('_', ' '), Sentence(insight.Text), InsightState(insight.State)));
            }
        }

        foreach (var section in sections.Where(s => s.Key != "plans"))
        {
            foreach (var line in Take(section, section.Key == "lessons" ? 2 : MaxDigestLinesPerSection))
            {
                lines.Add(new FactLine(string.Empty, "digest", section.Key.Replace('_', ' '), Sentence(line)));
            }
        }

        while (lines.Sum(l => l.Text.Length + 40) > maxChars && lines.Count > 1 && lines[^1].Section == "digest")
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return new BriefingFacts(BriefingFacts.Weekly, week, false, [], lines, null, digestId, insights?.Evidence.RunKeys ?? []);
    }

    private static IEnumerable<string> Take(DigestSection? section, int count) =>
        section is null ? [] : section.Lines.Take(Math.Max(0, section.Lines.Count - 1)).Take(count);

    private string Sentence(string text)
    {
        var clean = Clean(text, MaxLine);
        return clean.EndsWith('.') || clean.EndsWith('!') || clean.EndsWith('?') ? clean : clean + ".";
    }

    private static string InsightState(string state) => state.ToLowerInvariant() switch
    {
        "unavailable" => FactLine.Unavailable,
        "insufficient" => FactLine.Insufficient,
        "not_evaluable" or "notevaluable" or "not evaluable" => FactLine.NotEvaluable,
        _ => FactLine.Ok,
    };

    private string Levels(string symbol, PlanItemView ticket)
    {
        if (ticket.Levels is { } l)
        {
            var parts = new List<string> { Format($"{symbol} reference price {l.EntryRef:0.00}") };
            if (l.Stop is { } stop)
            {
                parts.Add(Format($"stop {stop:0.00}"));
            }

            if (l.Target is { } target)
            {
                parts.Add(Format($"target {target:0.00}"));
            }

            if (l.RewardRisk is { } rr)
            {
                parts.Add(Format($"reward-to-risk {rr:0.00}"));
            }

            return string.Join(", ", parts) + $", {l.Basis}.";
        }

        return $"{symbol} stop rule: {Clean(ticket.Stop, 120)}; target rule: {Clean(ticket.Target, 120)}.";
    }

    private FactLine Evidence(string symbol, PlanItemView ticket)
    {
        var e = ticket.Evidence;
        var unavailable = e.Unavailable.Count > 0 ? $" Unavailable inputs: {string.Join(", ", e.Unavailable.Select(u => Clean(u, 60)))}." : string.Empty;
        var pWin = e.PWin is { } p ? Format($"pWin {p * 100:0.#}% ({Clean(e.PWinBasis, 80)})") : "pWin unavailable";
        var expectancy = e.ConservativeExpectancyR is { } x ? Format($"conservative expectancy {x:0.00}R") : "conservative expectancy unavailable";
        var text = Format($"{symbol} cell {Clean(e.CellKey, 80)} has {e.Trades} trades, {pWin}, {expectancy}.") + unavailable;
        return new FactLine(symbol, "ticketdetail", "evidence", text, e.Unavailable.Count > 0 || e.PWin is null ? FactLine.Unavailable : FactLine.Ok);
    }

    private static string Format(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    private string Clean(string text, int max)
    {
        var cleaned = UntrustedText.Clean(text ?? string.Empty);
        _flags += cleaned.InjectionFlags;
        var value = System.Text.RegularExpressions.Regex.Replace(cleaned.Text.Trim(), @"\s+", " ", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));
        return value.Length <= max ? value : value[..max].TrimEnd();
    }
}
