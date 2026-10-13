using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Actions.Rules;
using XDrishti.Application.Portfolio.Returns;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Actions;

/// <summary>
/// The I/O around the rules (ADR 0022): reads the open holdings with their lots, the sectors and the basket memberships of their instruments, the thresholds and the owner's decisions (one read each) and, only when some holding is old enough for the
/// lagging-Nifty rule, the benchmark index's day bars; runs the pure rules (<see cref="ActionRuleSet"/>) and applies the decisions. No per-holding reads.
/// </summary>
internal sealed class ActionEvaluator(
    IHoldingQueries holdings,
    IHoldingClassifications classifications,
    IActionSettings settings,
    IActionStates states,
    IPortfolioBenchmark benchmarks,
    IPortfolioPrices prices,
    ITradingCalendar calendar,
    TimeProvider clock)
{
    /// <summary>
    /// Evaluates the open holdings of one account, or without an account those of every active Portfolio account (only the ones that count in totals with <paramref name="totalsOnly"/>). The whole account is always read — never one bucket — because
    /// weights and sector shares are shares of the account's value.
    /// </summary>
    public async ValueTask<ActionEvaluation> EvaluateAsync(Guid? accountId, bool totalsOnly, CancellationToken cancellationToken)
    {
        var today = IndiaTime.Today(clock.GetUtcNow());
        var thresholds = (await settings.GetAsync(cancellationToken)).Values;
        var data = await holdings.ListAsync(new HoldingFilter(accountId, null, "open", null, TotalsOnly: totalsOnly), cancellationToken);
        var classification = await ClassificationLoader.LoadAsync(classifications, data, sectors: true, baskets: true, cancellationToken);
        var inBasket = classification.Baskets.Where(b => b.Value.Count > 0).Select(b => b.Key).ToHashSet();
        var context = new ActionContext(data, today, thresholds, classification.Sectors, inBasket);

        var (indexCloses, tradingCalendar) = await IndexOfAsync(context, cancellationToken);
        var (hits, laggingUnavailable) = ActionRuleSet.Evaluate(context, indexCloses, tradingCalendar);

        var live = await states.ListLiveAsync(cancellationToken);
        var byKey = live.ToDictionary(s => new ActionKey(s.Rule, s.Key));
        var items = hits.Select(h => new EvaluatedItem(h.Key, Decided(h.Item, byKey.GetValueOrDefault(h.Key), today), h.BucketIds)).ToList();
        return new ActionEvaluation(data, items, laggingUnavailable, live);
    }

    /// <summary>Applies the owner's decision: <c>done</c> stays done; a snooze holds until its date (the first day the item shows again), then the item is open.</summary>
    internal static ActionItem Decided(ActionItem item, ActionStateRow? decision, DateOnly today) => decision switch
    {
        { State: ActionDecision.Done } => item with { State = ActionItemStates.Done },
        { State: ActionDecision.Snoozed, SnoozedUntil: { } until } when until > today => item with { State = ActionItemStates.Snoozed, SnoozedUntil = until },
        _ => item,
    };

    /// <summary>The benchmark index's adjusted closes and the trading calendar, read only when a holding is old enough for the lagging rule; nulls when not needed or when there is no benchmark.</summary>
    private async ValueTask<(IReadOnlyList<DailyClose>? Closes, TradingCalendar? Calendar)> IndexOfAsync(ActionContext context, CancellationToken cancellationToken)
    {
        var eligible = LaggingNiftyRule.Eligible(context);
        if (eligible.Count == 0 || await benchmarks.GetAsync(cancellationToken) is not { } configured)
        {
            return (null, null);
        }

        var earliest = eligible.SelectMany(h => h.Lots).Min(l => l.AcquiredOn);
        var stored = await prices.ReadDailyAsync([configured.Instrument.Id], earliest.AddDays(-GetPortfolioHistoryHandler.LookbackDays), context.Today, cancellationToken);
        var closes = stored.TryGetValue(configured.Instrument.Id, out var own) ? AdjustedCloses.Adjust(own.Closes, AdjustmentFactors.From(own.Actions)) : [];
        return (closes, await calendar.GetAsync(cancellationToken));
    }
}
