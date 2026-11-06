namespace XDrishti.Domain.Learning;

/// <summary>
/// The pure decisions around a kill rule (spec CF-12 D2 and D3, ADR 0219). Only a rule that <see cref="KillRuleState.Fires"/> makes a proposal; <see cref="KillRuleState.NotEvaluated"/>, <see cref="KillRuleState.Held"/> and <see cref="KillRuleState.Holds"/> never do.
/// The automatic setting may execute a proposal only when it is ON and the proposal is not proposal-only (the CF-09 failure boundary). A condition counts as cleared only when every rule that fired now <b>holds</b>: a rule that cannot be evaluated is not a pass,
/// and neither is a rule that fires or is held. Nothing here reads a clock, a store or a setting.
/// </summary>
public static class KillDecision
{
    /// <summary>Whether a verdict makes a proposal.</summary>
    public static bool MakesProposal(KillRuleState state) => state == KillRuleState.Fires;

    /// <summary>Whether the automatic setting may execute a proposal: it must be ON and the proposal must not be proposal-only.</summary>
    public static bool MayExecuteAutomatically(bool autoSettingOn, bool proposalOnly) => autoSettingOn && !proposalOnly;

    /// <summary>
    /// Whether the failing condition behind a pause has cleared: every rule in <paramref name="firedRules"/> reads <see cref="KillRuleState.Holds"/> now, and no rule at all fires or is held. An empty <paramref name="firedRules"/> (a pause no kill rule made) has no
    /// condition to clear and is cleared if nothing fires now.
    /// </summary>
    public static bool IsCleared(IReadOnlyCollection<string> firedRules, IReadOnlyList<(string Rule, KillRuleState State)> now)
    {
        ArgumentNullException.ThrowIfNull(firedRules);
        ArgumentNullException.ThrowIfNull(now);
        if (now.Any(v => v.State is KillRuleState.Fires or KillRuleState.Held))
        {
            return false;
        }

        return firedRules.All(rule => now.Any(v => v.Rule == rule && v.State == KillRuleState.Holds));
    }
}
