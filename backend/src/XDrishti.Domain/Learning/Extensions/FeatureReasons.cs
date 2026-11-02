namespace XDrishti.Domain.Learning.Extensions;

/// <summary>The closed reasons a value of <c>ev-1</c> or <c>fu-1</c> is NaN (P15-05, ADR 0185). Counted per feature in the extraction record; a NaN is never filled from a neighbour, a sector or a default.</summary>
public static class FeatureReasons
{
    /// <summary>No results filing (or no fact, no shareholding group) is visible at the decision instant, or the reaction session of the newest one is still ahead.</summary>
    public const string NoFiling = "no_filing";

    /// <summary>Reserved for a run that refuses facts of a parser that has not passed real filings; today such facts are read, labelled and counted in <c>parser_unverified</c> instead.</summary>
    public const string ParserUnverified = "parser_unverified";

    /// <summary>No single basis (consolidated or standalone) has every quarter the feature needs, though together they do.</summary>
    public const string BasisMixed = "basis_mixed";

    /// <summary>Fewer quarters, sessions or earlier groups than the feature needs.</summary>
    public const string ShortHistory = "short_history";

    /// <summary>No results calendar row is visible, or the trading calendar holds no session to count with.</summary>
    public const string NoCalendar = "no_calendar";

    /// <summary>No stored NIFTY 50 close for a session the feature needs.</summary>
    public const string NoContext = "no_context";

    /// <summary>No stored close of the instrument for a session the feature needs.</summary>
    public const string NoBars = "no_bars";

    /// <summary>The inputs are there but the quantity is undefined: a standard deviation of 0, a base of 0 or less, a TTM EPS of 0 or less. Not a pass and not a zero.</summary>
    public const string Degenerate = "degenerate";

    public static readonly IReadOnlyList<string> All = [NoFiling, ParserUnverified, BasisMixed, ShortHistory, NoCalendar, NoContext, NoBars, Degenerate];
}
