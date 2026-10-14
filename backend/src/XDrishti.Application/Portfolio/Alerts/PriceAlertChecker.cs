using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Alerts;

/// <summary>
/// The quote poll's use of the price alerts (ADR 0022): which alerts to quote, and triggering the ones the tick's quote answer hits. The condition is evaluated on the tick's last price only (<see cref="PriceAlert.Holds"/>: equality counts); a spike between
/// two ticks is not seen, and an alert whose instrument has no quote in the answer stays active. Every alert that is hit becomes <c>triggered</c> with the price and time <b>in the same transaction as its audit event</b>, by a conditional update on
/// <c>status = 'active'</c>, so an alert triggers at most once even when runs overlap (the run that loses the race records nothing). Reaches the alerts only through <see cref="IPriceAlerts"/>.
/// </summary>
internal sealed class PriceAlertChecker(IPriceAlerts alerts, IAccountEventLog events, IPortfolioUnitOfWork unitOfWork, ILogger<PriceAlertChecker> logger)
{
    public const string Actor = "system:holdings-quotes";

    /// <summary>The audit actor of an alert triggered by a tick of the live feed (ADR 0024), distinguishable from <see cref="Actor"/>, the 5-minute quote poll.</summary>
    public const string LiveActor = "system:live-feed";

    /// <summary>The active alerts (at most 200), read once per tick: their instruments join the quoted set and the same list is checked against the answer.</summary>
    public ValueTask<IReadOnlyList<ActiveAlert>> ListActiveAsync(CancellationToken cancellationToken) => alerts.ListActiveAsync(cancellationToken);

    /// <summary>Triggers the <paramref name="active"/> alerts that the quoted last prices (by instrument) hit; returns how many this call triggered. The audit actor is <see cref="Actor"/> (the quote poll).</summary>
    public ValueTask<int> CheckAsync(IReadOnlyList<ActiveAlert> active, IReadOnlyDictionary<Guid, decimal> lastPrices, DateTimeOffset at, CancellationToken cancellationToken) =>
        CheckAsync(active, lastPrices, at, Actor, cancellationToken);

    /// <summary>Triggers the <paramref name="active"/> alerts that the last prices (by instrument) hit, recording <paramref name="actor"/> in the audit event; returns how many this call triggered.</summary>
    public async ValueTask<int> CheckAsync(IReadOnlyList<ActiveAlert> active, IReadOnlyDictionary<Guid, decimal> lastPrices, DateTimeOffset at, string actor, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(lastPrices);
        if (active.Count == 0)
        {
            return 0;
        }

        var hits = active
            .Where(a => lastPrices.TryGetValue(a.InstrumentId, out var price) && price > 0m && PriceAlert.Holds(a.Direction, a.Level, price))
            .ToList();
        List<ActiveAlert> triggered = [];
        if (hits.Count > 0)
        {
            await unitOfWork.RunAsync(
                async token =>
                {
                    // The work may run again after a transient failure of the transaction: it starts from nothing each time.
                    triggered.Clear();
                    foreach (var hit in hits)
                    {
                        var price = lastPrices[hit.InstrumentId];
                        if (!await alerts.TryTriggerAsync(hit.Id, price, at, token))
                        {
                            continue;
                        }

                        events.Record(AccountEvent.Record(
                            AccountEventType.PriceAlertTriggered,
                            null,
                            $"Price alert triggered: {hit.Symbol} {AlertText.Condition(hit.Direction)} {AlertText.Price(hit.Level)} (last price {AlertText.Price(price)})",
                            actor,
                            at));
                        triggered.Add(hit);
                    }

                    await unitOfWork.SaveAsync(token);
                    return Result.Success();
                },
                cancellationToken);
            foreach (var hit in triggered)
            {
                AlertsLog.Triggered(logger, hit.Id, hit.Symbol);
            }
        }

        var quoted = active.Count(a => lastPrices.ContainsKey(a.InstrumentId));
        AlertsLog.Checked(logger, active.Count, quoted, triggered.Count);
        return triggered.Count;
    }
}
