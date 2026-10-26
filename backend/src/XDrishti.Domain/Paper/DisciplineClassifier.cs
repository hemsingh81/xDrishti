using XDrishti.Domain.Planning;

namespace XDrishti.Domain.Paper;

/// <summary>
/// Puts one primary ticket in exactly one of the seven categories (spec P10-03, criterion 3). Pure: the ticket's newest state, its stored paper trades and skips decide; nothing is read or written. A ticket sized for several accounts is one ticket.
/// </summary>
public static class DisciplineClassifier
{
    /// <summary>Classifies <paramref name="ticket"/> given the stored trades and skips of exactly this ticket.</summary>
    public static TicketOutcome Classify(ReviewTicket ticket, IReadOnlyList<ReviewTrade> trades, IReadOnlyList<ReviewSkip> skips)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        ArgumentNullException.ThrowIfNull(trades);
        ArgumentNullException.ThrowIfNull(skips);
        var reduced = ticket.Allocations.Any(a => a.Approved < a.Allocated) || trades.Any(t => t.Qty < t.QtyAllocated);
        TicketOutcome Of(string category, string? skip = null, string? why = null) => new(ticket, category, trades, skip, why, reduced);

        switch (ticket.State)
        {
            case TicketState.Rejected:
                return Of(DisciplineCategory.Rejected);
            case TicketState.Lapsed:
                return Of(DisciplineCategory.Lapsed);
            case TicketState.Invalidated:
                return Of(DisciplineCategory.InvalidatedPreopen);
            case TicketState.Proposed or TicketState.Modified:
                return Of(DisciplineCategory.Pending, why: "undecided");
        }

        var accounts = ticket.Allocations.Select(a => a.AccountId).Concat(trades.Select(t => t.AccountId)).Concat(skips.Select(s => s.AccountId)).Distinct().ToList();
        var settled = accounts.Count > 0 && accounts.All(id => trades.Any(t => t.AccountId == id) || skips.Any(s => s.AccountId == id));
        if (!settled)
        {
            return Of(DisciplineCategory.Pending, why: "awaiting_capture");
        }

        if (trades.Any(t => !t.Closed))
        {
            return Of(DisciplineCategory.Pending, why: "open");
        }

        if (trades.Count == 0)
        {
            return Of(DisciplineCategory.ApprovedNotEntered, skips.Select(s => s.Code).Order(StringComparer.Ordinal).First());
        }

        return Of(reduced ? DisciplineCategory.SizedDown : DisciplineCategory.Followed);
    }
}
