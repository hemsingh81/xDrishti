namespace XDrishti.Domain.Planning;

/// <summary>
/// The plan state machine as a pure table (spec P7-03, ADR 0083): Draft to Published; Published to InReview, Approved, Rejected or Lapsed; InReview to Approved, Rejected or Lapsed; Approved to Armed or Expired; Armed to Expired.
/// Eleven moves. A plan is Approved only when no ticket is left open and at least one is approved; Rejected or Lapsed only when none is open and none approved, so the plan state can never say more than its tickets.
/// </summary>
public static class PlanLifecycle
{
    private static readonly (PlanState From, PlanState To)[] Legal =
    [
        (PlanState.Draft, PlanState.Published),
        (PlanState.Published, PlanState.InReview),
        (PlanState.Published, PlanState.Approved),
        (PlanState.Published, PlanState.Rejected),
        (PlanState.Published, PlanState.Lapsed),
        (PlanState.InReview, PlanState.Approved),
        (PlanState.InReview, PlanState.Rejected),
        (PlanState.InReview, PlanState.Lapsed),
        (PlanState.Approved, PlanState.Armed),
        (PlanState.Approved, PlanState.Expired),
        (PlanState.Armed, PlanState.Expired),
    ];

    /// <summary>Every legal move.</summary>
    public static IReadOnlyList<(PlanState From, PlanState To)> Moves => Legal;

    /// <summary>The states reachable from <paramref name="from"/>.</summary>
    public static IReadOnlyList<PlanState> Next(PlanState from) => [.. Legal.Where(m => m.From == from).Select(m => m.To)];

    /// <summary>Whether the table has this move.</summary>
    public static bool IsLegal(PlanState from, PlanState to) => Legal.Contains((from, to));

    /// <summary>Why the move is refused; empty when it may be recorded.</summary>
    public static IReadOnlyList<string> Refusals(PlanState from, PlanState to, PlanFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (!IsLegal(from, to))
        {
            var next = Next(from);
            return [next.Count == 0
                ? $"A {from.Code()} plan cannot move: {from.Code()} is final."
                : $"A {from.Code()} plan cannot move to {to.Code()}; it can move to {string.Join(" or ", next.Select(s => s.Code()))}."];
        }

        var reasons = new List<string>();
        switch (to)
        {
            case PlanState.Approved when facts.Open > 0 || facts.Approved == 0:
                reasons.Add("A plan is approved when no ticket is left undecided and at least one is approved.");
                break;
            case PlanState.Rejected or PlanState.Lapsed when facts.Open > 0 || facts.Approved > 0:
                reasons.Add($"A plan is {to.Code()} only when no ticket is undecided and none is approved.");
                break;
            case PlanState.Armed when facts.Armed == 0:
                reasons.Add("A plan is armed when at least one ticket is armed.");
                break;
            case PlanState.InReview when facts.Approved + facts.Rejected == 0:
                reasons.Add("A plan is in review once the owner has decided on a ticket.");
                break;
        }

        return reasons;
    }
}
