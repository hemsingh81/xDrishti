using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Checklist;

namespace XDrishti.Application.Abstractions.Autonomy;

/// <summary>
/// The pre-trade checklist results of a stored ticket, one per account it is sized for (spec P7-05, P7-12). The registered source is <c>StoredChecklistSource</c> (P7-10), which reads the standings the plan stored;
/// a ticket whose record is missing or unreadable gets none and is never approved.
/// </summary>
public interface IChecklistSource
{
    /// <summary>The unconfirmed checklist results, or an empty list when they cannot be built.</summary>
    ValueTask<IReadOnlyList<ChecklistResult>> ForTicketAsync(PlanItemRow item, CancellationToken cancellationToken);
}
