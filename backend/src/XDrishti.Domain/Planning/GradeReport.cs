using System.Globalization;

namespace XDrishti.Domain.Planning;

/// <summary>
/// The pure report of graded tickets (P7-13, ADR 0090; doc 10 section 8). Top-K takes, for each plan, the K best-ranked primary and reserve tickets (the selector ranks only those; rejected candidates are a group of their own). A ticket that
/// did not trigger or cannot be graded adds 0 R and is counted. The figures are sums and means of stored R, so they can be reproduced by hand.
/// </summary>
public static class GradeReport
{
    /// <summary>Below this many triggered tickets a set is only indicative.</summary>
    public const int IndicativeBelow = 30;

    private static readonly int?[] Ks = [1, 3, 5, null];

    /// <summary>Builds the report.</summary>
    public static GradeReportData Build(IReadOnlyList<GradedItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var ranked = items.Where(i => i.Group != TicketGroup.Rejected).ToList();
        var curves = Ks.Select(k => Curve(k, ranked)).ToList();
        var groups = Enum.GetValues<TicketGroup>().Select(g => Stats(g.Code(), items.Where(i => i.Group == g))).ToList();
        var decisions = Enum.GetValues<DecisionGroup>().Select(d => Stats(d.ToString().ToLowerInvariant(), ranked.Where(i => DecisionGroups.Of(i.State) == d))).ToList();
        var primary = groups.Single(g => g.Name == "primary");
        var approved = decisions.Single(d => d.Name == "approved");
        var comparisons = new List<GroupComparison>
        {
            Compare("Did the reserve do better than the primary?", groups.Single(g => g.Name == "reserve"), primary),
            Compare("Would the rejected candidates have done better than the primary?", groups.Single(g => g.Name == "rejected"), primary),
            Compare("Did the owner's rejections help (approved minus rejected by the owner)?", approved, decisions.Single(d => d.Name == "rejected") with { Name = "rejected by the owner" }),
            Compare("Did the lapses help (approved minus lapsed)?", approved, decisions.Single(d => d.Name == "lapsed")),
        };
        var plans = items.Select(i => i.PlanId).Distinct().Count();
        List<string> notes =
        [
            "Every figure is " + TicketGrade.Basis + ": a re-simulation with the simulator's fill rules, not an execution.",
            "A ticket that did not trigger or cannot be graded adds 0 R and is counted apart; R is summed over triggered tickets only.",
            "Fewer than " + IndicativeBelow.ToString(CultureInfo.InvariantCulture) + " triggered tickets in a set is indicative only; a difference is never a significance claim.",
            "A ticket modified and then approved counts as approved. There is no auto-approved group: nothing approves for the owner.",
        ];
        return new GradeReportData(TicketGrade.Basis, plans, curves, groups, decisions, comparisons, notes);
    }

    private static GradeCurve Curve(int? k, List<GradedItem> ranked)
    {
        var name = k is { } n ? "top-" + n.ToString(CultureInfo.InvariantCulture) : "top-all";
        var points = new List<CurvePoint>();
        var chosen = new List<GradedItem>();
        var cumulative = 0.0;
        foreach (var day in ranked.GroupBy(i => (i.Date, i.PlanId)).OrderBy(g => g.Key.Date).ThenBy(g => g.Key.PlanId))
        {
            var top = day.OrderBy(i => i.Rank).Take(k ?? int.MaxValue).ToList();
            chosen.AddRange(top);
            var dayR = top.Where(i => i.Grade.HasR).Sum(i => i.Grade.R!.Value);
            cumulative += dayR;
            points.Add(new CurvePoint(day.Key.PlanId, day.Key.Date, top.Count, top.Count(i => i.Grade.HasR), dayR, cumulative));
        }

        return new GradeCurve(name, points, Stats(name, chosen));
    }

    private static GradeStats Stats(string name, IEnumerable<GradedItem> set)
    {
        var all = set.ToList();
        var graded = all.Where(i => i.Grade.HasR).Select(i => i.Grade.R!.Value).ToList();
        var total = graded.Sum();
        var note = graded.Count < IndicativeBelow ? "indicative only: " + graded.Count.ToString(CultureInfo.InvariantCulture) + " triggered ticket(s), fewer than " + IndicativeBelow.ToString(CultureInfo.InvariantCulture) : null;
        return new GradeStats(
            name, all.Count, graded.Count, all.Count(i => i.Grade.Status == GradeStatus.NotTriggered), all.Count(i => i.Grade.Status == GradeStatus.NotGradable), total,
            graded.Count == 0 ? null : total / graded.Count, graded.Count == 0 ? null : (double)graded.Count(r => r > 0) / graded.Count, note);
    }

    private static GroupComparison Compare(string question, GradeStats left, GradeStats right)
    {
        if (left.MeanR is not { } a || right.MeanR is not { } b)
        {
            return new GroupComparison(question, left.Name, right.Name, null, "not_enough_data", "a set has no triggered ticket");
        }

        var diff = a - b;
        var verdict = diff > 0 ? "first_better" : diff < 0 ? "second_better" : "equal";
        var small = left.Triggered < IndicativeBelow || right.Triggered < IndicativeBelow;
        var note = string.Create(CultureInfo.InvariantCulture, $"{left.Name}: {left.Triggered} triggered; {right.Name}: {right.Triggered} triggered") + (small ? "; indicative only, a set has fewer than " + IndicativeBelow.ToString(CultureInfo.InvariantCulture) + " triggered tickets" : string.Empty);
        return new GroupComparison(question, left.Name, right.Name, diff, verdict, note);
    }
}
