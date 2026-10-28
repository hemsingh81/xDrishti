using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Planning;

namespace XDrishti.Application.LiveTrading;

/// <summary>The live side of the report from stored rows: outcomes of the range, the executions, the plan grades and the strategy of each ticket. Reads only.</summary>
internal sealed class LivePairSource(ILiveCaptureStore capture, ILiveExecutionStore executions, LiveTicketSource tickets, IGradeStore grades) : ILivePairSource
{
    private const int PageSize = 500;

    public async ValueTask<IReadOnlyList<LiveTrip>> TripsAsync(Guid accountId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var outcomes = new List<StoredOutcome>();
        for (var skip = 0; ; skip += PageSize)
        {
            var page = await capture.ListOutcomesAsync(accountId, from, to, skip, PageSize, cancellationToken);
            outcomes.AddRange(page.Items);
            if (page.Items.Count < PageSize)
            {
                break;
            }
        }

        var byOutcome = new Dictionary<long, StoredLiveExecution>();
        for (var skip = 0; ; skip += PageSize)
        {
            var page = await executions.ListExecutionsAsync(accountId, from, to, skip, PageSize, cancellationToken);
            foreach (var e in page.Items)
            {
                byOutcome[e.Row.OutcomeId] = e;
            }

            if (page.Items.Count < PageSize)
            {
                break;
            }
        }

        var ticketsByItem = new Dictionary<long, LiveTicket>();
        foreach (var date in outcomes.Select(o => o.ForDate).Distinct())
        {
            foreach (var t in await tickets.ForDateAsync(date, accountId, cancellationToken))
            {
                ticketsByItem[t.ItemId] = t;
            }
        }

        var stored = await grades.ForPlansAsync([.. ticketsByItem.Values.Select(t => t.PlanId).Distinct()], cancellationToken);
        return [.. outcomes.OrderBy(o => o.ForDate).ThenBy(o => o.ItemId).Select(o =>
        {
            ticketsByItem.TryGetValue(o.ItemId, out var ticket);
            var grade = stored.GetValueOrDefault(o.ItemId)?.Grade.Grade;
            return new LiveTrip(
                o.ItemId, o.AccountId, o.ForDate, ticket?.Symbol ?? string.Empty, ticket is null ? string.Empty : ticket.Plan.Side == Domain.Research.Side.Buy ? "buy" : "sell",
                string.IsNullOrEmpty(ticket?.Strategy) ? "unknown" : ticket.Strategy, o.Outcome.Outcome, o.Outcome.PlanGradeStatus, grade is { HasR: true } ? grade.R : null, byOutcome.GetValueOrDefault(o.Id));
        })];
    }
}
