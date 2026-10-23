using XDrishti.Domain.Checklist;
using XDrishti.Domain.Planning;

namespace XDrishti.Domain.Autonomy;

/// <summary>A reserve ticket that the pre-open check could promote.</summary>
/// <param name="Ticket">Its facts (group reserve).</param>
/// <param name="Rank">Its place in the plan; lower is first.</param>
/// <param name="Checklists">Its checklist results.</param>
public sealed record PromotionCandidate(AutoApprovalTicket Ticket, int Rank, IReadOnlyList<ChecklistResult> Checklists);

/// <summary>
/// Promotion from the reserve at the pre-open check (spec P7-12, P7-00 question 5), pure and gated by <c>autonomy.promote_from_reserve</c>, default OFF. Promotion arms a ticket the owner never reviewed, so it is an auto approval
/// by another name: a reserve ticket qualifies only on the same terms as <see cref="AutoApprovalPolicy"/> (stored settings, lower-bound P(win) at the threshold, no unavailable input, every check passed on every account, daily cap),
/// at most one per freed slot, best rank first. With the switch OFF nothing is ever promoted. This function only chooses; the pre-open check (P7-11) records it and must add its own database gate, none exists yet, and no order exists.
/// </summary>
public static class ReservePromotion
{
    /// <summary>The ids of the reserve tickets to promote, in promotion order.</summary>
    public static IReadOnlyList<long> Select(AutonomySettings settings, int freedSlots, IReadOnlyList<PromotionCandidate> candidates, AutoApprovalContext context)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(context);
        if (!settings.PromoteFromReserve || freedSlots <= 0)
        {
            return [];
        }

        var chosen = new List<long>();
        var used = context.AutoApprovedToday;
        foreach (var candidate in candidates.OrderBy(c => c.Rank))
        {
            if (chosen.Count >= freedSlots)
            {
                break;
            }

            var ask = context with { AutoApprovedToday = used };
            if (AutoApprovalPolicy.Reasons(candidate.Ticket, candidate.Checklists, settings, ask, TicketGroup.Reserve, checkTiming: false).Count == 0)
            {
                chosen.Add(candidate.Ticket.ItemId);
                used++;
            }
        }

        return chosen;
    }
}
