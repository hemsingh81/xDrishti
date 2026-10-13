using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Actions;

/// <summary>An item as evaluated: its key, the item with the owner's decision applied, and the buckets of the holdings it is about (for the bucket scope).</summary>
internal sealed record EvaluatedItem(ActionKey Key, ActionItem Item, IReadOnlyList<Guid> BucketIds);

/// <summary>
/// One evaluation of the Action center over a set of open holdings: every item the rules produce (whatever its state), the holdings the lagging rule could not judge, and the decisions that were read with them.
/// </summary>
internal sealed record ActionEvaluation(
    IReadOnlyList<HoldingData> Holdings,
    IReadOnlyList<EvaluatedItem> Items,
    IReadOnlyList<HoldingData> LaggingUnavailable,
    IReadOnlyList<ActionStateRow> LiveStates)
{
    /// <summary>
    /// The <c>done</c> decisions whose condition no longer holds (ADR 0022 reconciliation): no rule produces their item now, although this evaluation covers it — a holding that is among the evaluated ones, or an account that has evaluated holdings.
    /// With <paramref name="fullScope"/> (the evaluation read every active Portfolio account: the quotes tick, a sync, a threshold change) a decision about a holding that is not among the open holdings at all (it was closed, or its account left the Portfolio)
    /// is stale too, and so is one about an account without open holdings: if the holding is opened again later (a sync reopens the same row) its item must start open. The read of one scope (<c>false</c>) stays conservative and leaves those alone, because
    /// the holding may simply be outside the scope. Always left alone: decisions about conditions the evaluation could not judge (the lagging rule without a benchmark, a price-based rule for a holding without a price).
    /// </summary>
    public IReadOnlyList<ActionStateRow> StaleDone(bool fullScope)
    {
        var done = LiveStates.Where(s => s.State == ActionDecision.Done).ToList();
        if (done.Count == 0)
        {
            return [];
        }

        var produced = Items.Select(i => i.Key).ToHashSet();
        var unjudged = LaggingUnavailable.Select(h => ActionKey.ForHolding(ActionRuleNames.LaggingNifty, h.Id)).ToHashSet();
        var holdings = Holdings.ToDictionary(h => h.Id);
        var accounts = Holdings.Select(h => h.AccountId).ToHashSet();
        List<ActionStateRow> stale = [];
        foreach (var row in done)
        {
            var key = new ActionKey(row.Rule, row.Key);
            if (produced.Contains(key) || unjudged.Contains(key))
            {
                continue;
            }

            if (ActionRuleNames.IsHoldingRule(row.Rule))
            {
                if (key.HoldingId is not { } id)
                {
                    continue;
                }

                if (holdings.TryGetValue(id, out var holding))
                {
                    if (holding.IsPriced || row.Rule == ActionRuleNames.NotInBasket)
                    {
                        stale.Add(row);
                    }
                }
                else if (fullScope)
                {
                    stale.Add(row);
                }
            }
            else if (key.AccountId is { } account && (accounts.Contains(account) || fullScope))
            {
                stale.Add(row);
            }
        }

        return stale;
    }
}
