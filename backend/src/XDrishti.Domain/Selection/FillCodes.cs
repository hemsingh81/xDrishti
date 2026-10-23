namespace XDrishti.Domain.Selection;

/// <summary>Stable codes of the portfolio-fill checks (doc 10 section 5).</summary>
public static class FillCodes
{
    /// <summary>The open book is not known: nothing can be checked, so nothing is taken.</summary>
    public const string Book = "book";

    /// <summary>The plan holds <c>MaxPlanItems</c> tickets (trigger mode).</summary>
    public const string PlanItems = "plan_items";

    /// <summary>New entries per day (plan mode).</summary>
    public const string EntriesPerDay = "entries_per_day";

    /// <summary>Open positions including carried swing.</summary>
    public const string OpenPositions = "open_positions";

    /// <summary>One position per symbol.</summary>
    public const string OnePerSymbol = "one_per_symbol";

    /// <summary>SELL on an instrument held long-term.</summary>
    public const string HeldLongTerm = "held_long_term";

    /// <summary>Risk of the single trade.</summary>
    public const string PerTradeRisk = "per_trade_risk";

    /// <summary>Total open risk.</summary>
    public const string TotalRisk = "total_risk";

    /// <summary>Net long or net short risk.</summary>
    public const string NetExposure = "net_exposure";

    /// <summary>Positions per sector.</summary>
    public const string Sector = "sector";

    /// <summary>Intraday / swing mix.</summary>
    public const string Mix = "mix";

    /// <summary>Positions per basket.</summary>
    public const string Basket = "basket";

    /// <summary>Pair correlation with the book and the picks.</summary>
    public const string Correlation = "correlation";

    /// <summary>Admissible, but the reserve list is full.</summary>
    public const string ReserveFull = "reserve_full";
}
