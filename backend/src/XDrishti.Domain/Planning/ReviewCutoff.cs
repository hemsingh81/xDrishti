namespace XDrishti.Domain.Planning;

/// <summary>What the review cutoff does (spec P7-03, ADR 0083): the indexes of the tickets that lapse and the state the plan takes.</summary>
/// <param name="LapseIndexes">Indexes (into the list given) of the tickets nobody decided on.</param>
/// <param name="Plan">Approved when the owner approved at least one ticket, else Rejected when the owner rejected all of them and none lapsed, else Lapsed.</param>
public sealed record CutoffDecision(IReadOnlyList<int> LapseIndexes, PlanState Plan);

/// <summary>The default at the review cutoff is to lapse, never to approve (doc 04 §4.2, <c>unreviewed_policy: lapse</c>). Pure.</summary>
public static class ReviewCutoff
{
    /// <summary>Decides on the tickets of a plan. Rejected-group candidates are not tickets and are ignored.</summary>
    public static CutoffDecision Decide(IReadOnlyList<(TicketGroup Group, TicketState State)> tickets)
    {
        ArgumentNullException.ThrowIfNull(tickets);
        var lapse = new List<int>();
        for (var i = 0; i < tickets.Count; i++)
        {
            if (tickets[i].Group != TicketGroup.Rejected && TicketLifecycle.CutoffDefault(tickets[i].State) is not null)
            {
                lapse.Add(i);
            }
        }

        var states = tickets.Where(t => t.Group != TicketGroup.Rejected).Select(t => t.State).ToList();
        var facts = PlanFacts.Of(states);
        var plan = facts.Approved > 0 ? PlanState.Approved : lapse.Count == 0 && facts.Rejected > 0 ? PlanState.Rejected : PlanState.Lapsed;
        return new CutoffDecision(lapse, plan);
    }
}
