using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Groups;

namespace XDrishti.Application.Portfolio.Actions.Rules;

/// <summary>
/// <b>Sector concentration</b>: one sector above <c>ConcentrationPct</c> % of an account's priced value is one <c>medium</c> item per account and sector, with the share and the largest holding in it. The sector is the instrument master's; holdings without a sector
/// ("Unclassified") never form a sector, and holdings without a usable price add no value. The item is about the account, not a holding (its key is the account and a hash of the sector). Pure.
/// </summary>
internal static class SectorConcentrationRule
{
    private const decimal Hundred = 100m;

    public static IReadOnlyList<RuleHit> Evaluate(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var t = context.Thresholds;
        var classification = new Classification(context.Sectors, new Dictionary<Guid, IReadOnlyList<BasketRef>>());
        var hits = new List<RuleHit>();
        foreach (var account in AccountPositions.Of(context.Holdings))
        {
            if (account.Value <= 0m)
            {
                continue;
            }

            foreach (var slice in HoldingPartitions.Partition(account.Holdings, GroupDimension.Sector, classification))
            {
                if (slice.Key == HoldingPartitions.NoneKey)
                {
                    continue;
                }

                var sectorValue = GroupCalculator.ValueOf([.. slice.Holdings]) ?? 0m;
                var share = sectorValue / account.Value * Hundred;
                if (share <= t.ConcentrationPct)
                {
                    continue;
                }

                var largest = slice.Holdings
                    .Select(h => (Holding: h, Value: GroupCalculator.ValueOf([h]) ?? 0m))
                    .OrderByDescending(x => x.Value)
                    .ThenBy(x => x.Holding.TradingSymbol, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.Holding.Id)
                    .First();
                var shown = Math.Round(share, 4, MidpointRounding.AwayFromZero);
                var accountName = largest.Holding.AccountName;
                var numbers = new ActionNumbers(
                    ThresholdPct: t.ConcentrationPct,
                    Positions: slice.Holdings.Count,
                    Sector: slice.Name,
                    SectorSharePct: shown,
                    SectorValue: sectorValue,
                    LargestSymbol: largest.Holding.TradingSymbol,
                    LargestHoldingId: largest.Holding.Id,
                    LargestValue: largest.Value);
                hits.Add(ItemFactory.ForAccount(
                    ActionKey.ForSector(account.AccountId, slice.Name),
                    ActionSeverities.Medium,
                    largest.Holding,
                    "Sector concentration",
                    $"{slice.Name} is {ActionText.Percent(shown)} of {accountName}'s value (limit {ActionText.Percent(t.ConcentrationPct)}); the largest holding is {largest.Holding.TradingSymbol} ({ActionText.Rupees(largest.Value)})",
                    numbers,
                    share - t.ConcentrationPct,
                    [.. slice.Holdings.Select(h => h.BucketId).Distinct()]));
            }
        }

        return hits;
    }
}
