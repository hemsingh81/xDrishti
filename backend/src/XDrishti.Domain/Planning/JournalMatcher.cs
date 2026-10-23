using System.Globalization;
using XDrishti.Domain.Market;

namespace XDrishti.Domain.Planning;

/// <summary>
/// Matches the fills the read-only sync stored to the approved tickets of one or more planned sessions (P7-14, ADR 0093). Pure and deterministic: the same fills and tickets give the same links, whatever the order they come in.
/// <list type="bullet">
/// <item>Pass 1, <b>entries across all sessions first</b>: a fill is an entry of a ticket when account, symbol, side and day all agree and the ticket was approved before the fill. A fill matches at most one ticket; a ticket may have several fills.
/// When <b>more than one</b> approved ticket fits a fill nothing is guessed: it is left unmatched with the reason. A fill that is an entry on its own day is never in the exit pool of an earlier session.</item>
/// <item>Pass 2, <b>exits</b>: tickets in plan order (session, rank, item, account). An exit is a fill of the other side, same account and symbol, strictly after the first entry fill (on the same day only when both times are known), at most
/// <see cref="MaxExitDays"/> days later, taken first-in first-out up to the entered quantity. <b>A fill goes whole to the first ticket that takes it</b> (its link quantity capped at what that ticket had open); the next ticket's remaining
/// quantity stays open and waits for another fill. So a trade has exactly one link.</item>
/// <item>Every other fill of a journaled session is <b>discretionary</b>: the owner's own trade, no ticket.</item>
/// </list>
/// Shortcut, stated: a sale of shares held before the ticket cannot be told from the ticket's own exit here; the handler checks the account's earlier net position and the grader flags an exit fill larger than the entered quantity as uncertain.
/// Upgrade when per-lot FIFO is available.
/// </summary>
public static class JournalMatcher
{
    /// <summary>How many days after the entry an exit fill may be (calendar days).</summary>
    public const int MaxExitDays = 30;

    /// <summary>Matches the <paramref name="fills"/> of one session.</summary>
    public static JournalResult Match(DateOnly date, IReadOnlyList<JournalTicket> tickets, IReadOnlyList<JournalFill> fills) => Match([date], tickets, fills);

    /// <summary>Matches the <paramref name="fills"/> of the journaled <paramref name="dates"/> (and later exits) to the <paramref name="tickets"/> of those planned sessions.</summary>
    public static JournalResult Match(IReadOnlyList<DateOnly> dates, IReadOnlyList<JournalTicket> tickets, IReadOnlyList<JournalFill> fills)
    {
        ArgumentNullException.ThrowIfNull(dates);
        ArgumentNullException.ThrowIfNull(tickets);
        ArgumentNullException.ThrowIfNull(fills);
        var days = dates.ToHashSet();
        var ordered = fills.OrderBy(f => f.Date).ThenBy(f => f.Time ?? TimeOnly.MaxValue).ThenBy(f => f.TransactionId).ToList();
        var mine = tickets.Where(t => days.Contains(t.ForDate)).OrderBy(t => t.ForDate).ThenBy(t => t.Rank).ThenBy(t => t.ItemId).ThenBy(t => t.AccountId).ToList();
        var links = new List<JournalLink>();
        var unmatched = new List<JournalUnmatched>();
        var taken = new HashSet<Guid>();
        var entries = new Dictionary<(long Item, Guid Account), List<JournalFill>>();

        foreach (var fill in ordered.Where(f => days.Contains(f.Date)))
        {
            var fit = mine.Where(t => t.ForDate == fill.Date && t.AccountId == fill.AccountId && t.Symbol == fill.Symbol && t.Side == fill.Side).ToList();
            if (fit.Count > 1)
            {
                unmatched.Add(new JournalUnmatched(fill, "ambiguous: " + fit.Count.ToString(CultureInfo.InvariantCulture) + " approved tickets fit this account, instrument, side and day (items " + string.Join(", ", fit.Select(t => t.ItemId)) + "); not guessed"));
                taken.Add(fill.TransactionId);
            }
            else if (fit.Count == 1 && !BeforeApproval(fill, fit[0]))
            {
                var t = fit[0];
                links.Add(new JournalLink(fill, JournalRole.Entry, t.ItemId, fill.AccountId, fill.Qty, "matched by account, instrument, side and day"));
                taken.Add(fill.TransactionId);
                if (!entries.TryGetValue((t.ItemId, t.AccountId), out var list))
                {
                    entries[(t.ItemId, t.AccountId)] = list = [];
                }

                list.Add(fill);
            }
        }

        // Exits, ticket by ticket in plan order, first-in first-out. A fill that any ticket has taken is gone from the pool: one trade, one link.
        var exitIds = new HashSet<Guid>();
        foreach (var ticket in mine.Where(t => entries.ContainsKey((t.ItemId, t.AccountId))))
        {
            var entry = entries[(ticket.ItemId, ticket.AccountId)];
            var open = entry.Sum(f => f.Qty);
            TimeOnly? firstTime = entry.Any(f => f.Time is null) ? null : entry.Min(f => f.Time);
            foreach (var fill in ordered.Where(f => !taken.Contains(f.TransactionId) && !exitIds.Contains(f.TransactionId) && f.AccountId == ticket.AccountId && f.Symbol == ticket.Symbol && f.Side != ticket.Side))
            {
                if (open <= 0)
                {
                    break;
                }

                if (!After(fill, ticket.ForDate, firstTime) || fill.Date > ticket.ForDate.AddDays(MaxExitDays))
                {
                    continue;
                }

                var take = Math.Min(open, fill.Qty);
                open -= take;
                exitIds.Add(fill.TransactionId);
                links.Add(new JournalLink(fill, JournalRole.Exit, ticket.ItemId, ticket.AccountId, take, "closes the position of the ticket, after its entry"));
            }
        }

        foreach (var fill in ordered.Where(f => days.Contains(f.Date) && !taken.Contains(f.TransactionId) && !exitIds.Contains(f.TransactionId)))
        {
            links.Add(new JournalLink(fill, JournalRole.Discretionary, null, fill.AccountId, fill.Qty, "no approved ticket for this account, instrument, side and day (or the fill came before the approval)"));
        }

        return new JournalResult(links, unmatched);
    }

    private static bool BeforeApproval(JournalFill fill, JournalTicket ticket) =>
        fill.Time is { } time && new DateTimeOffset(fill.Date.ToDateTime(time), IndiaTime.Offset) < ticket.ApprovedAt;

    private static bool After(JournalFill fill, DateOnly entryDate, TimeOnly? entryTime) =>
        fill.Date != entryDate ? fill.Date > entryDate : fill.Time is { } t && entryTime is { } e && t > e;
}
