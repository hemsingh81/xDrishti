using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Learning;

/// <summary>
/// Reads the newest stored plan of each of the last N planned sessions with its items and hands the codes to the pure <see cref="SmartRulesEvidence"/> (P8-13). It reads only the stored plan record through <see cref="IPlanStore"/>; nothing is computed
/// about what a rule would have done, and no outcome is invented.
/// </summary>
internal sealed class GetSmartRulesEvidenceHandler(IPlanStore plans) : IQueryHandler<GetSmartRulesEvidenceQuery, Result<SmartRulesView>>
{
    /// <summary>The most planned sessions one read counts.</summary>
    public const int MaxPlans = 60;

    private const string Source = "The reason and unavailable codes the plan builder stored on each plan item. Structured plan flags (P8-14) are not stored yet.";

    /// <summary>The display facts of each rule; the keys must equal <see cref="SmartRulesEvidence.RuleIds"/> (a test pins it).</summary>
    internal static readonly Dictionary<string, (string Name, string Outcome, string Command, string? NotRecorded)> Rules = new(StringComparer.Ordinal)
    {
        ["gate"] = ("Market gate", "R by VIX and breadth bucket needs market context, which is not stored (P3-01d/e).", "P3-01d/e (market context)", null),
        ["throttle"] = ("Equity throttle", "Equity with and without the throttle needs real taken trades, which exist only after paper and live trading. The throttle cuts risk, so it moves no candidate.", "P10/P11 (taken trades)", null),
        ["meta"] = ("Meta-labelling", "R taken against R skipped needs a champion model and a stored policy evaluation (xd learn policy --store). The plan rows leave no code when the policy ran.", "xd learn policy", "the plan rows carry no code for this rule"),
        ["windows"] = ("Entry windows", "No entry-window study is stored; the owner runs and stores one with xd learn windows --store. R by entry hour is the calendar report of P8-10.", "xd learn windows", "the plan rows carry no code for this rule"),
        ["correlation"] = ("Correlation screen", "A correlation matrix needs the 60-day return table at plan time, which the plan does not keep; only a code for pairs not checked is stored.", "P8-14 (plan flags)", null),
    };

    public async ValueTask<Result<SmartRulesView>> HandleAsync(GetSmartRulesEvidenceQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Plans is < 1 or > MaxPlans)
        {
            return Result.Failure<SmartRulesView>(LearningErrors.Invalid([new FieldError("plans", $"Read 1 to {MaxPlans} plans.")]));
        }

        // Shortcut: IPlanStore lists newest first with no per-date or distinct-date read, so three times the sessions asked for are listed on the assumption of at most three versions per session; with more versions fewer sessions come back.
        // The answer says how many sessions it returned. A per-date read on the store would remove this.
        var listed = await plans.ListAsync(null, null, query.Plans * 3, cancellationToken);
        var chosen = listed.GroupBy(p => p.Plan.ForDate).Select(g => g.OrderByDescending(p => p.Plan.Version).First()).OrderByDescending(p => p.Plan.ForDate).Take(query.Plans).ToList();
        var facts = new List<PlanRuleFacts>();
        foreach (var plan in chosen)
        {
            var items = await plans.ItemsAsync(plan.Id, cancellationToken);
            facts.Add(new PlanRuleFacts(plan.Id, plan.Plan.ForDate, [.. items.Select(i => new PlanItemFacts(i.Item.Group.Code(), i.Item.Reasons, i.Item.Evidence.Unavailable))]));
        }

        var counts = SmartRulesEvidence.Build(facts);
        var empty = counts.PlansRead == 0;
        var limits = new List<string>
        {
            "A plan row stores a code only when a rule was unavailable, moved a candidate or flagged the plan; a rule that ran normally leaves nothing, so 'ran on' is not derived.",
            "Counting codes measures what the plan recorded, not what a rule would have done had its input existed.",
            "The flag rule_removed_half names no rule in the stored record (the rule is only in the banner text).",
            counts.PlansRead == 0
                ? "No planned session is stored."
                : FormattableString.Invariant($"{counts.PlansRead} planned sessions from {counts.From:yyyy-MM-dd} to {counts.To:yyyy-MM-dd} were read, of the {query.Plans} asked for; only the newest version of each session is counted."),
        };
        return Result.Success(new SmartRulesView(
            empty ? "empty" : "observed",
            counts.PlansRead,
            counts.From,
            counts.To,
            Source,
            [.. counts.Rules.Select(r => View(r, empty))],
            [.. counts.Flagged.Select(f => new FlaggedPlanView(f.PlanId, f.ForDate, f.Code))],
            limits));
    }

    private static SmartRuleView View(RuleCounts c, bool empty)
    {
        var (name, outcome, command, notRecorded) = Rules[c.Id];
        var outcomeView = new RuleOutcomeView("not_evaluable", null, outcome, command);
        var reasons = c.Reasons.Select(r => new ReasonCountView(r.Code, r.Count)).ToList();
        if (empty)
        {
            return new SmartRuleView(c.Id, name, "empty", null, null, null, null, reasons, "no plan stored yet", outcomeView);
        }

        if (!c.Recorded)
        {
            return new SmartRuleView(c.Id, name, "not_recorded", null, null, null, null, reasons, notRecorded, outcomeView);
        }

        return new SmartRuleView(c.Id, name, "observed", null, c.UnavailableOn, c.Id == "correlation" ? c.PartialOn : null, c.Moved, reasons, null, outcomeView);
    }
}
