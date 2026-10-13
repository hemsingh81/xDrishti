namespace XDrishti.Application.Portfolio.Actions.Rules;

/// <summary>
/// <b>Not in a basket</b>: an open holding that is in the instrument master but in no live basket is one <c>info</c> item (a prompt to file it). The portfolio's own buckets are not baskets. It is not price-based, so a holding without a price can fire.
/// A holding that is not in the master has no instrument to put in a basket and never fires. Pure.
/// </summary>
internal static class NotInBasketRule
{
    public static IReadOnlyList<RuleHit> Evaluate(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var hits = new List<RuleHit>();
        foreach (var holding in context.Holdings.Where(h => h.IsOpen && h.InstrumentId is { } id && !context.InBasket.Contains(id)))
        {
            var figures = context.Returns(holding);
            hits.Add(ItemFactory.ForHolding(
                ActionRuleNames.NotInBasket,
                ActionSeverities.Info,
                holding,
                "Not in a basket",
                "This holding is not in any basket yet.",
                new ActionNumbers(Invested: figures.Invested, Value: figures.Value),
                figures.Value ?? 0m));
        }

        return hits;
    }
}
