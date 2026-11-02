namespace XDrishti.Strategies.Model;

/// <summary>
/// The closed set of filter names of schema 1 (doc 11 §2). <c>not_event_day</c> is evaluated by the simulator from a results-calendar snapshot (P15-06, ADR 0182); <c>min_turnover</c> is accepted with a warning and not evaluated: nothing has the liquidity data of the universe layer yet.
/// </summary>
internal static class FilterNames
{
    /// <summary>No position is open across a results event (the simulator reads the results calendar snapshot of the run, P15-06).</summary>
    public const string NotEventDay = "not_event_day";

    /// <summary>Require a minimum turnover (needs the liquidity data of the universe layer, doc 10 §2).</summary>
    public const string MinTurnover = "min_turnover";

    /// <summary>Every filter name, in a fixed order.</summary>
    public static readonly IReadOnlyList<string> All = [NotEventDay, MinTurnover];
}
