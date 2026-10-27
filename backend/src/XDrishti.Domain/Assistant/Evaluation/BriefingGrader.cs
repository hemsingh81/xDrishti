using System.Text.RegularExpressions;
using XDrishti.Domain.Assistant.Text;
using XDrishti.Domain.Common;

namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>
/// Grades the model's texts for one plan (spec P9-09 AC 5): the guard of P9-06 is the checker, the independent audit looks for what it lets through. The attempts are the texts the model wrote: the first, and the repaired one when the first was refused.
/// Raw and repaired are two readings of the same run, so one run of at most two calls measures both. Pure.
/// </summary>
public static class BriefingGrader
{
    public static PlanGrade Grade(PlanCase plan, IReadOnlyList<string> attempts, int maxWords)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(attempts);
        if (attempts.Count == 0)
        {
            return new PlanGrade(plan.Id, false, false, false, false, new ValueList<string>([]), new ValueList<string>([]));
        }

        var first = TextGuard.Check(attempts[0], plan.Facts, maxWords);
        var accepted = first.Passed ? attempts[0] : attempts.Skip(1).Take(1).FirstOrDefault(a => TextGuard.Check(a, plan.Facts, maxWords).Passed);
        var absent = accepted is null ? [] : IndependentNumberAudit.Absent(accepted, plan.Facts);
        var counted = accepted ?? attempts[^1];
        return new PlanGrade(
            plan.Id, first.Passed, accepted is not null, absent.Count > 0, plan.Facts.Tickets.All(t => HasSymbol(counted, t)),
            new ValueList<string>(first.Findings.Select(f => f.Code).Distinct(StringComparer.Ordinal)), new ValueList<string>(absent));
    }

    private static bool HasSymbol(string text, string symbol) =>
        Regex.IsMatch(text, @"(?<![A-Za-z0-9&_-])" + Regex.Escape(symbol) + @"(?![A-Za-z0-9&_])", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
}
