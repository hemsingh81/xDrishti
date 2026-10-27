using System.Text;

namespace XDrishti.Domain.Assistant.Text;

/// <summary>
/// The fixed-template text (spec P9-06 AC 2): the plan state and date, every primary and reserve ticket in plan order with one line each, the risks, the notes, and the pinned line. It restates the facts and nothing else, so it is the same on every repeat and it passes the guard
/// by construction (the oracle of the guard's tests). With no ticket it says so and lists why. Pure.
/// </summary>
public static class DeterministicBriefing
{
    public static string Build(BriefingFacts facts, int maxWords = GuardRules.DailyMaxWords)
    {
        ArgumentNullException.ThrowIfNull(facts);
        foreach (var details in new[] { true, false })
        {
            var risks = facts.Lines.Where(l => l.Section == "risk").ToList();
            while (true)
            {
                var text = Compose(facts, risks, details);
                if (TextGuard.WordCount(text) <= maxWords)
                {
                    return text;
                }

                if (risks.Count == 0)
                {
                    break;
                }

                risks.RemoveAt(risks.Count - 1);
            }
        }

        return Compose(facts, [], false);
    }

    private static string Compose(BriefingFacts facts, List<FactLine> risks, bool details)
    {
        var builder = new StringBuilder();
        foreach (var line in facts.Lines.Where(l => l.Section == "plan"))
        {
            builder.Append(line.Text).Append('\n');
        }

        var tickets = facts.Lines.Where(l => l.Section == "ticket").ToList();
        if (tickets.Count > 0)
        {
            builder.Append("Tickets:\n");
            foreach (var ticket in tickets)
            {
                builder.Append("- ").Append(ticket.Text);
                if (details)
                {
                    foreach (var detail in facts.Lines.Where(l => l.Section == "ticketdetail" && l.Scope == ticket.Scope))
                    {
                        builder.Append(' ').Append(detail.Text);
                    }
                }

                builder.Append('\n');
            }
        }

        if (risks.Count > 0)
        {
            builder.Append("Risks:\n");
            foreach (var risk in risks)
            {
                builder.Append("- ").Append(risk.Text).Append('\n');
            }
        }

        var notes = facts.Lines.Where(l => l.Section == "note").ToList();
        if (notes.Count > 0)
        {
            builder.Append("Notes:\n");
            foreach (var note in notes)
            {
                builder.Append("- ").Append(note.Text).Append('\n');
            }
        }

        return builder.Append('\n').Append(BriefingWording.DailyFooter).ToString();
    }
}
