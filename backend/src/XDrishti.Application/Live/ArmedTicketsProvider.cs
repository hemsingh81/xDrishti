using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Planning;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Live;

/// <summary>
/// The instruments of the armed tickets of today's plan (spec P7-11, ADR 0096, ADR 0024 §2), in quote mode, so the owner sees their prices while they are watched. Read only: a ticket is Armed in the plan record, and the instrument
/// joins the feed's set only for as long as it stays Armed on the plan of today (India date); it leaves when the ticket is Expired, Invalidated or Triggered or when the day ends. An armed ticket ranks after every other member of the set, so
/// when the cap cuts, the armed ticket is the one refused (the existing cut warning) and no holding, alert or index is displaced. The provider never fails the set: if the plan record cannot be read it proposes nothing and logs once per call.
/// Nothing here places, modifies or cancels an order, and it needs no new endpoint or credential (the feed's read-only subscriptions are the same).
/// </summary>
internal sealed class ArmedTicketsProvider(IArmedTicketReader armed, IInstrumentSecurities securities, TimeProvider time, ILogger<ArmedTicketsProvider> logger) : IActiveSetProvider
{
    /// <summary>Orders armed tickets among themselves, rank 1 first. At most 1, so that merging with a held share (the builder keeps the larger value) can never lift a holding above the others.</summary>
    private static decimal Order(int rank) => 1m / Math.Max(1, rank);

    public async ValueTask<IReadOnlyList<ActiveSetCandidate>> ListAsync(CancellationToken cancellationToken)
    {
        try
        {
            var tickets = await armed.ListAsync(IndiaTime.Today(time.GetUtcNow()), cancellationToken);
            if (tickets.Count == 0)
            {
                return [];
            }

            var found = await securities.FindAsync([.. tickets.Select(t => t.Symbol).Distinct(StringComparer.Ordinal)], cancellationToken);
            var candidates = new List<ActiveSetCandidate>();
            foreach (var ticket in tickets)
            {
                if (found.TryGetValue(ticket.Symbol, out var security))
                {
                    candidates.Add(new ActiveSetCandidate(security.InstrumentId, FeedSegments.NseEquity, security.SecurityId, FeedMode.Quote, ActiveSetReason.ArmedTicket, Order(ticket.Rank)));
                }
                else
                {
                    PreOpenLog.NotQuotable(logger, ticket.ItemId);
                }
            }

            return candidates;
        }
#pragma warning disable CA1031 // The armed tickets are an add-on to the set: a failure to read them must never take the holdings, alerts and indices out of the feed.
        catch (Exception failure) when (failure is not OperationCanceledException)
#pragma warning restore CA1031
        {
            PreOpenLog.ProviderFailed(logger, failure.GetType().Name);
            return [];
        }
    }
}
