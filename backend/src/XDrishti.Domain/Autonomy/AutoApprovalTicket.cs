using XDrishti.Domain.Planning;

namespace XDrishti.Domain.Autonomy;

/// <summary>The facts of one ticket that the auto-approval rule reads (spec P7-12). Plain data from the stored plan.</summary>
/// <param name="ItemId">The ticket.</param>
/// <param name="Symbol">The instrument.</param>
/// <param name="Group">Primary, reserve or rejected.</param>
/// <param name="State">The ticket's state now.</param>
/// <param name="PWinLower">The lower bound of the shrunk cell estimate of the win rate; null when not known. The only P(win) the rule reads (P7-00 question 4): the posterior mean and a model's figure are not used.</param>
/// <param name="UnavailableInputs">Inputs the plan could not use (the evidence block); any entry blocks an auto approval.</param>
/// <param name="AccountIds">The accounts the ticket is sized for; each needs a passing checklist.</param>
/// <param name="LiveAccountIds">The accounts among <paramref name="AccountIds"/> whose allocation is manual-live (spec P11-04): the ticket then also needs <c>live.auto_approve_tickets</c> ON in a stored snapshot, and those accounts the live checklist.</param>
public sealed record AutoApprovalTicket(long ItemId, string Symbol, TicketGroup Group, TicketState State, double? PWinLower, IReadOnlyList<string> UnavailableInputs, IReadOnlyList<string> AccountIds, IReadOnlyList<string>? LiveAccountIds = null);
