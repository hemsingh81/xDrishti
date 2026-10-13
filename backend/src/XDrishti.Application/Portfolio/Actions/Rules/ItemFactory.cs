using XDrishti.Application.Abstractions.Portfolio;

namespace XDrishti.Application.Portfolio.Actions.Rules;

/// <summary>Builds the items of the rules with the fields they share, so each rule only says what is particular to it.</summary>
internal static class ItemFactory
{
    /// <summary>An item about one holding. The alert and the basket buttons need the instrument master, so they are suggested only for a holding that is in it.</summary>
    public static RuleHit ForHolding(string rule, string severity, HoldingData holding, string title, string detail, ActionNumbers numbers, decimal size, bool suggestBasket = true)
    {
        var key = ActionKey.ForHolding(rule, holding.Id);
        List<string> suggestions = [ActionSuggestions.Open];
        if (holding.InstrumentId is not null)
        {
            suggestions.Add(ActionSuggestions.SetAlert);
            if (suggestBasket)
            {
                suggestions.Add(ActionSuggestions.AddToBasket);
            }
        }

        suggestions.Add(ActionSuggestions.Snooze);
        suggestions.Add(ActionSuggestions.Done);
        var item = new ActionItem(
            key.Id, rule, severity, holding.AccountId, holding.AccountName, holding.Id, holding.InstrumentId, holding.TradingSymbol, title, detail, numbers, suggestions, ActionItemStates.Open, null);
        return new RuleHit(key, item, size, [holding.BucketId]);
    }

    /// <summary>An item about an account (the concentration of a sector): no holding of its own.</summary>
    public static RuleHit ForAccount(ActionKey key, string severity, HoldingData anyHolding, string title, string detail, ActionNumbers numbers, decimal size, IReadOnlyList<Guid> bucketIds)
    {
        var item = new ActionItem(
            key.Id, key.Rule, severity, anyHolding.AccountId, anyHolding.AccountName, null, null, null, title, detail, numbers, [ActionSuggestions.Snooze, ActionSuggestions.Done], ActionItemStates.Open, null);
        return new RuleHit(key, item, size, bucketIds);
    }
}
