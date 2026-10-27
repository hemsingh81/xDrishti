using System.Text;

namespace XDrishti.Domain.Assistant.Text;

/// <summary>
/// The fixed-template weekly review (spec P9-06 AC 11): the plan-outcome lines, the report insights, then the digest's lines in order while the word limit allows, and the pinned last line. It restates the digest and the insights and applies nothing. Pure.
/// </summary>
public static class DeterministicWeeklyReview
{
    public static string Build(BriefingFacts facts, int maxWords = GuardRules.WeeklyMaxWords)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var ordered = facts.Lines.Where(l => l.Section == "plan").Concat(facts.Lines.Where(l => l.Section == "insight")).Concat(facts.Lines.Where(l => l.Section == "digest")).ToList();
        var footerWords = TextGuard.WordCount(BriefingWording.WeeklyFooter);
        var kept = new List<FactLine>();
        var words = 0;
        foreach (var line in ordered)
        {
            var cost = TextGuard.WordCount(line.Text) + 1;
            if (words + cost + footerWords > maxWords)
            {
                continue;
            }

            kept.Add(line);
            words += cost;
        }

        var builder = new StringBuilder();
        foreach (var line in kept)
        {
            builder.Append(line.Section == "plan" ? string.Empty : "- ").Append(line.Text).Append('\n');
        }

        return builder.Append('\n').Append(BriefingWording.WeeklyFooter).ToString();
    }
}
