using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Actions;
using XDrishti.Application.Portfolio.Alerts;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Portfolio;

/// <summary>
/// One notice of the Portfolio module for the top-bar bell. <c>Kind</c> is <c>price_alert_triggered</c> (<c>Severity</c> warn; <c>AlertId</c> and <c>InstrumentId</c> set) or <c>actions_need_decision</c> (info). <c>Id</c> is stable, so the bell keeps what was read by id.
/// </summary>
public sealed record PortfolioNotice(string Id, string Kind, string Severity, string Text, DateTimeOffset? At, Guid? AlertId, Guid? InstrumentId);

public sealed record PortfolioNotices(IReadOnlyList<PortfolioNotice> Items);

/// <summary>
/// The Portfolio notices for the bell, derived on read like the data and service notices (ADR 0022 §4, spec 16): <c>price_alert_triggered</c> for each alert that triggered in the last 7 days (id <c>portfolio:alert:{alertId}</c>; the text names the symbol, the
/// condition, the level and the trigger price) and <c>actions_need_decision</c> when the Action center has open items for the accounts that count in totals (id <c>portfolio:actions:{India date}</c>, so it is raised again at most once a day).
/// </summary>
public sealed record GetPortfolioNoticesQuery : IQuery<PortfolioNotices>;

internal sealed class GetPortfolioNoticesHandler(IPriceAlertStore alerts, ActionEvaluator evaluator, IPortfolioHistoryCache cache, TimeProvider clock, ILogger<GetPortfolioNoticesHandler> logger) : IQueryHandler<GetPortfolioNoticesQuery, PortfolioNotices>
{
    public const string AlertTriggered = "price_alert_triggered";

    public const string ActionsNeedDecision = "actions_need_decision";

    /// <summary>A triggered alert is listed for this many days.</summary>
    public const int AlertDays = 7;

    public async ValueTask<PortfolioNotices> HandleAsync(GetPortfolioNoticesQuery query, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        List<PortfolioNotice> notices = [];
        foreach (var alert in await alerts.ListTriggeredSinceAsync(now.AddDays(-AlertDays), cancellationToken))
        {
            var price = alert.TriggerPrice is { } p ? $" (price {AlertText.Price(p)})" : string.Empty;
            notices.Add(new PortfolioNotice(
                $"portfolio:alert:{alert.Id}",
                AlertTriggered,
                "warn",
                $"{alert.Symbol} is {AlertText.Condition(alert.Direction)} {AlertText.Price(alert.Level)}{price}",
                alert.TriggeredAt,
                alert.Id,
                alert.InstrumentId));
        }

        var open = await OpenItemsAsync(cancellationToken);
        if (open > 0)
        {
            var date = IndiaTime.Today(now).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var text = open == 1 ? "1 holding needs a decision" : string.Create(CultureInfo.InvariantCulture, $"{open} holdings need a decision");
            notices.Add(new PortfolioNotice($"portfolio:actions:{date}", ActionsNeedDecision, "info", text, now, null, null));
        }

        return new PortfolioNotices(notices);
    }

    /// <summary>
    /// The number of open Action-center items of the accounts that count in totals. The bell asks every minute and the count needs a whole evaluation (the same reads as the Action center and the XIRRs of the lagging rule), so it is kept in the
    /// portfolio cache for at most 60 seconds and forgotten at once by whatever drops the cached histories (holdings, lots, quotes, accounts, baskets) and by the decisions and threshold writes (they save through the same unit of work); a snooze that ends or a
    /// date that changes is seen within the minute. A failure to evaluate is logged and the count is left out, so the alert notices are still returned (cancellation is not swallowed).
    /// </summary>
    private async ValueTask<int> OpenItemsAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGet(OpenCountKey, out OpenCount? cached))
        {
            return cached!.Items;
        }

        try
        {
            var version = cache.Version;
            var evaluation = await evaluator.EvaluateAsync(null, totalsOnly: true, cancellationToken);
            var count = new OpenCount(evaluation.Items.Count(i => i.Item.State == ActionItemStates.Open));
            cache.Set(OpenCountKey, count, version);
            return count.Items;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // the alert notices must not be lost because the count of open items failed; the failure is logged
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ActionsLog.NoticeCountFailed(logger, ex);
            return 0;
        }
    }

    private sealed record OpenCount(int Items);

    private const string OpenCountKey = "portfolio-notices:open-items";
}
