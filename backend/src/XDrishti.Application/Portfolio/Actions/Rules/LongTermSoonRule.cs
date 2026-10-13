namespace XDrishti.Application.Portfolio.Actions.Rules;

/// <summary>
/// <b>Long-term soon</b>: an open lot with an unrealised gain that is 12 months old within the next <c>LongTermWindowDays</c> days (calendar, India date) makes one <c>info</c> item for its holding, listing the lots and the dates. A lot is 12 months old
/// on the same calendar date a year later (29 February → 28 February); a lot that is 12 months old already is not "soon". Informational only: the text asks the owner to check the tax impact before selling early and gives no tax advice. A lot that cost nothing (bonus or transferred-in shares) is profitable and is listed, with no gain percentage. Holdings without
/// a usable price never fire. Pure.
/// </summary>
internal static class LongTermSoonRule
{
    public static IReadOnlyList<RuleHit> Evaluate(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var window = context.Today.AddDays(context.Thresholds.LongTermWindowDays);
        var hits = new List<RuleHit>();
        foreach (var holding in context.Holdings.Where(h => h.IsOpen && h.IsPriced))
        {
            var lots = holding.Lots
                .Where(l => l.IsOpen && holding.LastPrice > l.UnitCost)
                .Select(l => (Lot: l, On: l.AcquiredOn.AddYears(1)))
                .Where(x => x.On > context.Today && x.On <= window)
                .OrderBy(x => x.On)
                .ThenBy(x => x.Lot.AcquiredOn)
                .ToList();
            if (lots.Count == 0)
            {
                continue;
            }

            var figures = context.Returns(holding);
            var shown = lots
                .Select(x => new LongTermLot(
                    x.Lot.AcquiredOn,
                    x.Lot.Qty,
                    x.On,
                    x.On.DayNumber - context.Today.DayNumber,
                    x.Lot.UnitCost > 0m ? Math.Round((holding.LastPrice - x.Lot.UnitCost) / x.Lot.UnitCost * 100m, 4, MidpointRounding.AwayFromZero) : null))
                .ToList();
            var first = shown[0];
            var count = ActionText.Plural(shown.Count, "profitable lot", "profitable lots");
            var numbers = new ActionNumbers(PnlPct: figures.PnlPct, Lots: shown);
            hits.Add(ItemFactory.ForHolding(
                ActionRuleNames.LongTermSoon,
                ActionSeverities.Info,
                holding,
                "Long-term soon",
                $"{count} turn 12 months old within {ActionText.Plural(context.Thresholds.LongTermWindowDays, "day", "days")} (the first on {ActionText.Date(first.LongTermOn)}). Check the tax impact before selling early.",
                numbers,
                context.Thresholds.LongTermWindowDays - first.DaysLeft));
        }

        return hits;
    }
}
