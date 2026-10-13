using XDrishti.Application.Portfolio.Groups;

namespace XDrishti.Application.Portfolio.Actions.Rules;

/// <summary>
/// <b>Overweight</b>: in an account with at least <c>MinPositions</c> priced positions, a holding whose share of the account's priced value is above max(<c>OverweightFloorPct</c> %, <c>OverweightMultiple</c> × the equal-weight share 100 / positions) is
/// <c>medium</c>. The weight and the limit are in the numbers. Holdings without a usable price are not positions and never fire. Pure.
/// </summary>
internal static class OverweightRule
{
    private const decimal Hundred = 100m;

    public static IReadOnlyList<RuleHit> Evaluate(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var t = context.Thresholds;
        var hits = new List<RuleHit>();
        foreach (var account in AccountPositions.Of(context.Holdings))
        {
            if (account.Holdings.Count < t.MinPositions || account.Value <= 0m)
            {
                continue;
            }

            var limit = Math.Max(t.OverweightFloorPct, t.OverweightMultiple * Hundred / account.Holdings.Count);
            foreach (var holding in account.Holdings)
            {
                var value = GroupCalculator.ValueOf([holding]) ?? 0m;
                var weight = value / account.Value * Hundred;
                if (weight <= limit)
                {
                    continue;
                }

                var shown = Math.Round(weight, 4, MidpointRounding.AwayFromZero);
                var numbers = new ActionNumbers(WeightPct: shown, ThresholdPct: Math.Round(limit, 4, MidpointRounding.AwayFromZero), Positions: account.Holdings.Count, Value: value);
                hits.Add(ItemFactory.ForHolding(
                    ActionRuleNames.Overweight,
                    ActionSeverities.Medium,
                    holding,
                    "Overweight",
                    $"{ActionText.Percent(shown)} of {holding.AccountName}'s value, above the {ActionText.Percent(limit)} limit ({account.Holdings.Count} positions)",
                    numbers,
                    weight - limit));
            }
        }

        return hits;
    }
}
