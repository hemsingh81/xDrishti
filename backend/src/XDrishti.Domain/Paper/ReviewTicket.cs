using XDrishti.Domain.Planning;

namespace XDrishti.Domain.Paper;

/// <summary>A ticket of the newest plan of a session with the facts the discipline report needs (spec P10-03): its newest state, the owner's decision and the stored grade. Plain data; nothing here moves a ticket.</summary>
/// <param name="PlanId">The plan.</param>
/// <param name="ItemId">The ticket.</param>
/// <param name="ForDate">The planned session.</param>
/// <param name="Group">Primary or reserve.</param>
/// <param name="Symbol">The symbol.</param>
/// <param name="Side">BUY or SELL.</param>
/// <param name="State">The newest state.</param>
/// <param name="ApprovalKind">owner or auto when an approval row exists; null otherwise.</param>
/// <param name="ConfirmedDespite">Whether the approval was given despite a failed check.</param>
/// <param name="Grade">The stored plan-price grade, or null when none is stored.</param>
/// <param name="Allocations">The size per paper account.</param>
public sealed record ReviewTicket(long PlanId, long ItemId, DateOnly ForDate, TicketGroup Group, string Symbol, string Side, TicketState State, string? ApprovalKind, bool ConfirmedDespite, TicketGrade? Grade, IReadOnlyList<ReviewAllocation> Allocations)
{
    /// <summary>Whether an approval row exists.</summary>
    public bool Approved => ApprovalKind is not null;
}
