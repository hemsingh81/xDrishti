using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Checklist;

namespace XDrishti.Application.Abstractions.Autonomy;

/// <summary>
/// The pre-trade checklist results of a stored ticket, one per account it is sized for (spec P7-05, P7-12). Until the review slice wires the inputs (accounts, holdings, limits) the registered source is
/// <c>NullChecklistSource</c>, which returns none: a ticket with no checklist is never auto-approved.
/// </summary>
public interface IChecklistSource
{
    /// <summary>The unconfirmed checklist results, or an empty list when they cannot be built.</summary>
    ValueTask<IReadOnlyList<ChecklistResult>> ForTicketAsync(PlanItemRow item, CancellationToken cancellationToken);
}
