using XDrishti.Domain.Selection.SmartRules;

namespace XDrishti.Domain.Learning;

/// <summary>
/// Counts, from the codes stored on plan rows, how often each smart rule was unavailable and how many candidates it moved to reserve (P8-13, ADR 0108). A plan row stores no code for a rule that ran normally, so "ran on N plans" is never derived; and no outcome is computed.
/// Pure and deterministic. A plan is counted once per code however many of its items carry it (the flag codes are copied onto every item).
/// </summary>
public static class SmartRulesEvidence
{
    /// <summary>The rule ids in display order.</summary>
    public static IReadOnlyList<string> RuleIds { get; } = ["gate", "throttle", "meta", "windows", "correlation"];

    private static readonly Dictionary<string, string[]> MovedBy = new(StringComparer.Ordinal)
    {
        ["gate"] = [SmartRuleCodes.ReserveGateNoTrade, SmartRuleCodes.ReserveGateReduced],
        ["correlation"] = [SmartRuleCodes.SameBetBetterTicket, SmartRuleCodes.SameBetOpenTrade],
    };

    private static readonly Dictionary<string, string> UnavailableCode = new(StringComparer.Ordinal)
    {
        ["gate"] = SmartRuleCodes.GateUnavailable,
        ["throttle"] = SmartRuleCodes.ThrottleUnavailable,
        ["correlation"] = SmartRuleCodes.CorrelationUnavailable,
    };

    /// <summary>Counts over <paramref name="plans"/> (one entry per planned session; the caller chooses the version).</summary>
    public static SmartRulesCounts Build(IReadOnlyList<PlanRuleFacts> plans)
    {
        ArgumentNullException.ThrowIfNull(plans);
        var rules = new List<RuleCounts>();
        foreach (var id in RuleIds)
        {
            var recorded = UnavailableCode.ContainsKey(id);
            var unavailable = 0;
            var partial = 0;
            var reasons = (MovedBy.GetValueOrDefault(id) ?? []).ToDictionary(c => c, _ => 0, StringComparer.Ordinal);
            foreach (var plan in plans)
            {
                if (recorded && plan.Items.Any(i => i.Unavailable.Contains(UnavailableCode[id], StringComparer.Ordinal)))
                {
                    unavailable++;
                }

                if (id == "correlation" && plan.Items.Any(i => i.Unavailable.Contains(SmartRuleCodes.CorrelationPartial, StringComparer.Ordinal)))
                {
                    partial++;
                }

                foreach (var item in plan.Items.Where(i => string.Equals(i.Group, "reserve", StringComparison.Ordinal)))
                {
                    foreach (var code in item.Reasons.Where(reasons.ContainsKey).Distinct(StringComparer.Ordinal))
                    {
                        reasons[code]++;
                    }
                }
            }

            rules.Add(new RuleCounts(id, recorded, unavailable, partial, reasons.Values.Sum(), [.. reasons.Select(kv => new ReasonCount(kv.Key, kv.Value))]));
        }

        var flagged = plans
            .Where(p => p.Items.Any(i => i.Unavailable.Contains(SmartRuleCodes.RuleRemovedHalf, StringComparer.Ordinal)))
            .OrderBy(p => p.ForDate).ThenBy(p => p.PlanId)
            .Select(p => new FlaggedPlan(p.PlanId, p.ForDate, SmartRuleCodes.RuleRemovedHalf))
            .ToList();
        return new SmartRulesCounts(plans.Count, plans.Count == 0 ? null : plans.Min(p => p.ForDate), plans.Count == 0 ? null : plans.Max(p => p.ForDate), rules, flagged);
    }
}
