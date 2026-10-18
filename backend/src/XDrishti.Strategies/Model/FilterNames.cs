namespace XDrishti.Strategies.Model;

/// <summary>The closed set of filter names of schema 1 (doc 11 §2). Both are accepted with a warning and not evaluated: nothing in P3 has the data (no results calendar, no liquidity data).</summary>
internal static class FilterNames
{
    /// <summary>Skip event days (needs the results calendar, P15).</summary>
    public const string NotEventDay = "not_event_day";

    /// <summary>Require a minimum turnover (needs the liquidity data of the universe layer, doc 10 §2).</summary>
    public const string MinTurnover = "min_turnover";

    /// <summary>Every filter name, in a fixed order.</summary>
    public static readonly IReadOnlyList<string> All = [NotEventDay, MinTurnover];
}
