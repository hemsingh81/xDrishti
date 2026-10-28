using XDrishti.Domain.Planning;

namespace XDrishti.Domain.LiveTrading;

/// <summary>What one fill of the day is: an entry or exit of a ticket, the owner's own trade, or a fill left unmatched with the reason (it fits two tickets). Exactly one of the shapes.</summary>
/// <param name="Fill">The fill.</param>
/// <param name="Role">Entry, exit or discretionary; null for an unmatched fill.</param>
/// <param name="ItemId">The ticket (plan item) of an entry or exit.</param>
/// <param name="Reason">Why, in words.</param>
public sealed record LiveFillLink(LiveFill Fill, JournalRole? Role, long? ItemId, string Reason)
{
    public bool IsUnmatched => Role is null;
}
