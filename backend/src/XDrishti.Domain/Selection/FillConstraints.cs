using System.Globalization;

namespace XDrishti.Domain.Selection;

/// <summary>
/// The portfolio limits of doc 10 section 5 (spec P7-02, ADR 0084). Defaults are the design's table; <c>MaxPerTradeRiskPct</c>, <c>MaxPlanItems</c> (design: "e.g. 5") and <c>ReserveSize</c> are stated assumptions.
/// </summary>
/// <param name="MaxEntriesPerDay">New entries per day.</param>
/// <param name="MaxOpenPositions">Open positions including carried swing.</param>
/// <param name="MaxTotalOpenRiskPct">Total open risk, percent of capital.</param>
/// <param name="MaxPerSector">Positions per sector.</param>
/// <param name="MaxPairCorrelation">Highest pair correlation allowed with any open position or pick.</param>
/// <param name="MaxIntraday">Intraday positions.</param>
/// <param name="MaxSwing">Swing positions.</param>
/// <param name="MaxNetLongRiskPct">Net long risk (long minus short), percent.</param>
/// <param name="MaxNetShortRiskPct">Net short risk (short minus long), percent.</param>
/// <param name="MaxPerTradeRiskPct">Risk of one trade, percent (assumption: 1.0, twice the standard profile's 0.5).</param>
/// <param name="MaxPerBasket">Optional limit per basket name; none by default. When set, a candidate whose baskets are not known is blocked.</param>
/// <param name="SkipSellOnHeldLongTerm">Skip SELL setups on instruments in the long-term holdings.</param>
/// <param name="CountEntriesOn">What the entries limit applies to.</param>
/// <param name="MaxPlanItems">Most tickets listed in trigger mode.</param>
/// <param name="ReserveSize">Most reserve entries.</param>
public sealed record FillConstraints(
    int MaxEntriesPerDay = 3,
    int MaxOpenPositions = 5,
    double MaxTotalOpenRiskPct = 3.0,
    int MaxPerSector = 2,
    double MaxPairCorrelation = 0.70,
    int MaxIntraday = 2,
    int MaxSwing = 2,
    double MaxNetLongRiskPct = 2.0,
    double MaxNetShortRiskPct = 2.0,
    double MaxPerTradeRiskPct = 1.0,
    IReadOnlyDictionary<string, int>? MaxPerBasket = null,
    bool SkipSellOnHeldLongTerm = true,
    EntriesCountMode CountEntriesOn = EntriesCountMode.Trigger,
    int MaxPlanItems = 5,
    int ReserveSize = 3)
{
    /// <summary>The basket limits in force; none by default.</summary>
    public IReadOnlyDictionary<string, int> BasketLimits => MaxPerBasket ?? new Dictionary<string, int>();

    /// <summary>The problems of the values; empty when the constraints can be used.</summary>
    public IReadOnlyList<string> Problems()
    {
        var p = new List<string>();
        Count(p, nameof(MaxEntriesPerDay), MaxEntriesPerDay);
        Count(p, nameof(MaxOpenPositions), MaxOpenPositions);
        Count(p, nameof(MaxPerSector), MaxPerSector);
        Count(p, nameof(MaxIntraday), MaxIntraday);
        Count(p, nameof(MaxSwing), MaxSwing);
        Count(p, nameof(MaxPlanItems), MaxPlanItems);
        Count(p, nameof(ReserveSize), ReserveSize);
        Pct(p, nameof(MaxTotalOpenRiskPct), MaxTotalOpenRiskPct);
        Pct(p, nameof(MaxNetLongRiskPct), MaxNetLongRiskPct);
        Pct(p, nameof(MaxNetShortRiskPct), MaxNetShortRiskPct);
        Pct(p, nameof(MaxPerTradeRiskPct), MaxPerTradeRiskPct);
        if (!double.IsFinite(MaxPairCorrelation) || MaxPairCorrelation is < -1 or > 1)
        {
            p.Add("MaxPairCorrelation must be a number from -1 to 1");
        }

        if (!Enum.IsDefined(CountEntriesOn))
        {
            p.Add("CountEntriesOn must be trigger or plan");
        }

        foreach (var (basket, max) in BasketLimits)
        {
            if (string.IsNullOrWhiteSpace(basket) || max is < 0 or > 1000)
            {
                p.Add(string.Create(CultureInfo.InvariantCulture, $"basket limit '{basket}' must have a name and a count from 0 to 1000"));
            }
        }

        return p.AsReadOnly();
    }

    private static void Count(List<string> p, string name, int v)
    {
        if (v is < 0 or > 1000)
        {
            p.Add(name + " must be from 0 to 1000");
        }
    }

    private static void Pct(List<string> p, string name, double v)
    {
        if (!double.IsFinite(v) || v is < 0 or > 100)
        {
            p.Add(name + " must be a number from 0 to 100");
        }
    }
}
