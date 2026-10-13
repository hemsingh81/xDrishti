namespace XDrishti.Application.Portfolio.Actions;

/// <summary>The ids of the Action-center rules (ADR 0022): part of the API and of the stored decisions — never rename one.</summary>
public static class ActionRuleNames
{
    public const string BelowCost = "below_cost";

    public const string LargeGain = "large_gain";

    public const string Overweight = "overweight";

    public const string SectorConcentration = "sector_concentration";

    public const string LongTermSoon = "long_term_soon";

    public const string LaggingNifty = "lagging_nifty";

    public const string NotInBasket = "not_in_basket";

    /// <summary>Every rule, in the order the owner reads them.</summary>
    public static IReadOnlyList<string> All { get; } =
        [BelowCost, LargeGain, Overweight, SectorConcentration, LongTermSoon, LaggingNifty, NotInBasket];

    /// <summary>
    /// The order of the rules within one severity, most pressing first: below cost, lagging Nifty, overweight, sector concentration, large gain, long-term soon, not in a basket. The sizes of different rules are in different units (percent, rupees, days), so
    /// items are never ordered by size across rules; the size orders the items of one rule.
    /// </summary>
    public static IReadOnlyList<string> ByPriority { get; } =
        [BelowCost, LaggingNifty, Overweight, SectorConcentration, LargeGain, LongTermSoon, NotInBasket];

    /// <summary>The place of a rule in <see cref="ByPriority"/>; unknown rules last.</summary>
    public static int Rank(string rule)
    {
        for (var i = 0; i < ByPriority.Count; i++)
        {
            if (ByPriority[i] == rule)
            {
                return i;
            }
        }

        return ByPriority.Count;
    }

    /// <summary>True for the rules whose item is one holding (their key is the holding id); the concentration rule's item is an account and a sector.</summary>
    public static bool IsHoldingRule(string rule) => rule != SectorConcentration && All.Contains(rule);
}
