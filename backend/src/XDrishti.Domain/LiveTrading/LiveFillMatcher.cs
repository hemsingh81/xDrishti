using XDrishti.Domain.Planning;

namespace XDrishti.Domain.LiveTrading;

/// <summary>
/// Matches the fills of one live session to the approved tickets (spec P11-01 decision 4, ADR 0149) with the unchanged <see cref="JournalMatcher"/>: account, instrument, side and the planned day, the ticket approved before the fill, a fill that fits two
/// tickets left unmatched with the reason. The tolerances never take part (they flag, see <see cref="TicketOutcomeClassifier"/>). Pure, and independent of the order the fills come in.
/// </summary>
public static class LiveFillMatcher
{
    public static IReadOnlyList<LiveFillLink> Match(DateOnly date, IReadOnlyList<JournalTicket> tickets, IReadOnlyList<LiveFill> fills)
    {
        ArgumentNullException.ThrowIfNull(tickets);
        ArgumentNullException.ThrowIfNull(fills);
        var byId = fills.ToDictionary(f => f.Id);
        var asJournal = fills.Select(f => new JournalFill(f.Id, f.AccountId, f.Symbol, f.Side, f.Date, f.Time, f.Qty, f.Price, 0m)).ToList();
        var result = JournalMatcher.Match(date, tickets, asJournal);
        var links = result.Links.Select(l => new LiveFillLink(byId[l.Fill.TransactionId], l.Role, l.ItemId, l.Reason)).ToList();
        links.AddRange(result.Unmatched.Select(u => new LiveFillLink(byId[u.Fill.TransactionId], null, null, u.Reason)));
        return [.. links.OrderBy(l => l.Fill.Date).ThenBy(l => l.Fill.Time ?? TimeOnly.MaxValue).ThenBy(l => l.Fill.Id)];
    }
}
