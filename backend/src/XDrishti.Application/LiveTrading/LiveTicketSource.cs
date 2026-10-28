using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.LiveTrading;

/// <summary>
/// The approved tickets with a manual-live allocation of one planned session, as the capture reads them (spec P11-01): the newest version of the date's plan, items the owner approved that went on from there, one ticket per live allocation. Reads only.
/// The plan entry and stop are the ticket's own levels (indicative from the close of the previous session), else the stored grade's.
/// </summary>
internal sealed class LiveTicketSource(IPlanStore plans)
{
    public const string ManualLiveMode = "manual_live";

    public async ValueTask<IReadOnlyList<LiveTicket>> ForDateAsync(DateOnly date, Guid? accountId, CancellationToken cancellationToken)
    {
        var plan = (await plans.ListAsync(date, null, 30, cancellationToken)).Where(r => r.Plan.ForDate == date).OrderByDescending(r => r.Plan.Version).ThenByDescending(r => r.Id).FirstOrDefault();
        if (plan is null)
        {
            return [];
        }

        var items = await plans.ItemsAsync(plan.Id, cancellationToken);
        var approvedAt = (await plans.HistoryAsync(plan.Id, cancellationToken))
            .Where(e => e.ItemId is not null && e.To == TicketState.Approved.Code())
            .GroupBy(e => e.ItemId!.Value)
            .ToDictionary(g => g.Key, g => g.Min(e => e.At));
        var tickets = new List<LiveTicket>();
        foreach (var item in items.Where(i => i.Item.Group != TicketGroup.Rejected && TicketLifecycle.ApprovedFamily.Contains(i.State) && approvedAt.ContainsKey(i.Id)))
        {
            if (TicketLifecycle.ParseSide(item.Item.Side) is not { } side)
            {
                continue;
            }

            foreach (var allocation in item.Item.Allocations.Where(a => a.Mode == ManualLiveMode && (accountId is null || a.AccountId == accountId)))
            {
                var levels = item.Item.Levels;
                var journal = new JournalTicket(item.Id, plan.Id, item.Item.Rank, allocation.AccountId, TradeRules.NormaliseSymbol(item.Item.Symbol), side, date, approvedAt[item.Id]);
                tickets.Add(new LiveTicket(journal, new TicketPlan(side, allocation.Quantity, levels?.EntryRef, levels?.Stop), plan.Id, item.Item.StrategyId));
            }
        }

        return tickets;
    }

    /// <summary>The live allocations of the date's newest plan, every non-rejected item (approved or not): the risk scale each was cut by and the ramp event it carries. Reads only (spec P11-06, C2).</summary>
    public async ValueTask<IReadOnlyList<(double Scale, long RampEventId)>> ScalesForDateAsync(DateOnly date, Guid accountId, CancellationToken cancellationToken)
    {
        var plan = (await plans.ListAsync(date, null, 30, cancellationToken)).Where(r => r.Plan.ForDate == date).OrderByDescending(r => r.Plan.Version).ThenByDescending(r => r.Id).FirstOrDefault();
        if (plan is null)
        {
            return [];
        }

        return [.. (await plans.ItemsAsync(plan.Id, cancellationToken)).Where(i => i.Item.Group != TicketGroup.Rejected)
            .SelectMany(i => i.Item.Allocations)
            .Where(a => a.Mode == ManualLiveMode && a.AccountId == accountId && a.RiskScale is not null && a.RampEventId is not null)
            .Select(a => (a.RiskScale!.Value, a.RampEventId!.Value))];
    }
}
