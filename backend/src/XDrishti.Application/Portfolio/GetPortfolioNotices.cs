using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Actions;
using XDrishti.Application.Portfolio.Alerts;
using XDrishti.Application.Portfolio.Attention;
using XDrishti.Application.Portfolio.Heal;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Portfolio;

/// <summary>
/// One notice of the Portfolio module for the top-bar bell. <c>Kind</c> is <c>price_alert_triggered</c> (<c>Severity</c> warn; <c>AlertId</c> and <c>InstrumentId</c> set), <c>actions_need_decision</c> (info) or - since ADR 0027 - an <b>attention item</b>:
/// <c>reconnect_dhan</c>, <c>sync_failing</c>, <c>holdings_need_review</c>, <c>trades_unexplained</c>, <c>dividends_pending</c>, <c>securities_unmatched</c> or <c>rows_dropped</c> (<c>Severity</c> error, warning or info; <c>AccountId</c> set). <c>Id</c> is stable and, for an attention item,
/// includes its situation, so a changed situation is a new item and the bell keeps what was read by id. Since ADR 0027 every notice also has a <c>Title</c>, at most two <c>Actions</c> (a route to open or an existing endpoint to call), <c>Since</c> (when it was first seen; null for a notice derived on read that is not remembered - <c>actions_need_decision</c> - whose id already changes with the India date, so a client that keys read state on id and since sees the same key on every poll)
/// and <c>Resolved</c>: an attention item whose condition stopped holding is kept for 24 hours with <c>Resolved</c> true and then dropped; resolved items are not counted in the badge. <c>At</c> is kept as it was (the time of the event, or <c>Since</c> for an attention item).
/// </summary>
public sealed record PortfolioNotice(
    string Id,
    string Kind,
    string Severity,
    string Text,
    DateTimeOffset? At,
    Guid? AlertId,
    Guid? InstrumentId,
    string? Title = null,
    IReadOnlyList<AttentionAction>? Actions = null,
    DateTimeOffset? Since = null,
    bool Resolved = false,
    Guid? AccountId = null);

public sealed record PortfolioNotices(IReadOnlyList<PortfolioNotice> Items);

/// <summary>
/// The Portfolio notices for the bell, read only (ADR 0030; the attention items are kept up to date by <see cref="IAttentionReconciler"/>), derived on read like the data and service notices (ADR 0022 §4, spec 16): <c>price_alert_triggered</c> for each alert that triggered in the last 7 days (id <c>portfolio:alert:{alertId}</c>; the text names the symbol, the
/// condition, the level and the trigger price) and <c>actions_need_decision</c> when the Action center has open items for the accounts that count in totals (id <c>portfolio:actions:{India date}</c>, so it is raised again at most once a day).
/// </summary>
public sealed record GetPortfolioNoticesQuery : IQuery<PortfolioNotices>;

internal sealed class GetPortfolioNoticesHandler(
    IPriceAlertStore alerts,
    ActionEvaluator evaluator,
    IPortfolioHistoryCache cache,
    IAttentionStore attentionStore,
    TimeProvider clock,
    ILogger<GetPortfolioNoticesHandler> logger) : IQueryHandler<GetPortfolioNoticesQuery, PortfolioNotices>
{
    /// <summary>A resolved attention item is kept for this long (the drawer's Resolved group), then dropped.</summary>
    public static readonly TimeSpan KeepResolved = TimeSpan.FromHours(24);

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
                alert.InstrumentId,
                "Price alert",
                [new AttentionAction("Open", AttentionActionKinds.Route, "/portfolio?alerts=triggered")],
                alert.TriggeredAt));
        }

        var open = await OpenItemsAsync(cancellationToken);
        if (open > 0)
        {
            var date = IndiaTime.Today(now).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var text = open == 1 ? "1 holding needs a decision" : string.Create(CultureInfo.InvariantCulture, $"{open} holdings need a decision");
            notices.Add(new PortfolioNotice($"portfolio:actions:{date}", ActionsNeedDecision, "info", text, now, null, null, "Holdings need a decision", [new AttentionAction("Open", AttentionActionKinds.Route, "/portfolio?focus=actions")]));
        }

        notices.AddRange(await AttentionNoticesAsync(now, cancellationToken));
        return new PortfolioNotices(notices);
    }

    /// <summary>
    /// The attention items (ADR 0027, ADR 0030): read as the reconciler (the healer tick, the end of a sync run) remembered them - this query writes nothing - and returned in the order error, warning, info, newest first, unresolved before resolved. A failure to read them
    /// is logged and the notices go without them (cancellation is not swallowed).
    /// </summary>
    private async ValueTask<IEnumerable<PortfolioNotice>> AttentionNoticesAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            var stored = await attentionStore.ListAsync(now, KeepResolved, cancellationToken);
            return stored
                .OrderBy(a => a.ResolvedAt is null ? 0 : 1)
                .ThenBy(a => AttentionSeverities.Rank(a.Severity))
                .ThenByDescending(a => a.Since)
                .ThenBy(a => a.Id, StringComparer.Ordinal)
                .Select(a => new PortfolioNotice(
                    a.Id,
                    a.Kind,
                    a.Severity,
                    a.Text,
                    a.Since,
                    null,
                    null,
                    a.Title,
                    AttentionActionsJson.Deserialize(a.ActionsJson),
                    a.Since,
                    a.ResolvedAt is not null,
                    a.AccountId))
                .ToList();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // the alert notices must not be lost because the attention items could not be worked out; the failure is logged
        catch (Exception ex)
#pragma warning restore CA1031
        {
            HealLog.AttentionFailed(logger, ex);
            return [];
        }
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
