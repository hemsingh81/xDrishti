namespace XDrishti.Domain.Baskets.Derived;

/// <summary>One of the fourteen behaviour baskets as data. <c>NotThis</c> is the one-line "what this basket is NOT".</summary>
public sealed record DerivedDefinition(
    int Id,
    string Name,
    string Description,
    string NotThis,
    int WindowDays,
    int MinBars,
    DerivedCadence Cadence,
    SelectionForm Form,
    bool NeedsIntraday = false,
    bool Ascending = false)
{
    /// <summary>The parameter key of this basket, for example <c>Basket1.TopK</c>.</summary>
    public string Key(string name) => $"Basket{Id}.{name}";

    public bool IsSlow => Cadence is DerivedCadence.Weekly or DerivedCadence.Monthly;
}
