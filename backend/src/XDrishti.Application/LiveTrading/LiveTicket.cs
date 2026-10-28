using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.LiveTrading;

/// <summary>One approved ticket of one live account for one planned session: the key the matcher uses, what the plan said, and the plan.</summary>
internal sealed record LiveTicket(JournalTicket Journal, TicketPlan Plan, long PlanId)
{
    public long ItemId => Journal.ItemId;

    public Guid AccountId => Journal.AccountId;

    public string Symbol => Journal.Symbol;
}
