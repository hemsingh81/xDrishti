namespace XDrishti.Domain.Execution;

/// <summary>What the mode guard knows about an account. A null is a fact that could not be read.</summary>
public sealed record DemotionFacts(bool? TradingRoleOn, bool? Archived);

public sealed record DemotionDecision(LadderMode Target, string Code);

/// <summary>
/// The system's way down (spec P12-07, D4): fail to the lower mode. An account above <c>paper</c> whose Trading role is off or that is archived goes to <c>paper</c>; a fact that cannot be read counts as the bad case, because the only thing a
/// demotion costs is a new promotion. The triggers of the automated modes (reconciliation breaks, a missing stop, caps) are added with those modes. Pure; it only ever answers a lower rung.
/// </summary>
public static class DemotionPolicy
{
    public static DemotionDecision? Evaluate(LadderMode current, DemotionFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (current == LadderMode.Paper)
        {
            return null;
        }

        if (facts.Archived != false)
        {
            return new DemotionDecision(LadderMode.Paper, facts.Archived is null ? "archived_unreadable" : "archived");
        }

        return facts.TradingRoleOn != true ? new DemotionDecision(LadderMode.Paper, facts.TradingRoleOn is null ? "role_unreadable" : "role_off") : null;
    }
}
