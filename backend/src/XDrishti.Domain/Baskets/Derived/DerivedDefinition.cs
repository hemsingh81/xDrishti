namespace XDrishti.Domain.Baskets.Derived;

/// <summary>One of the fourteen behaviour baskets as data. <c>NotThis</c> is the one-line "what this basket is NOT"; <c>Unit</c> is the unit its score is printed in: <c>ratio</c> (2 decimals), <c>percent</c> (a share, "2.10 %") or <c>rupees</c> (crore).</summary>
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
    bool Ascending = false,
    string Unit = "ratio")
{
    /// <summary>The parameter key of this basket, for example <c>Basket1.TopK</c>.</summary>
    public string Key(string name) => $"Basket{Id}.{name}";

    public bool IsSlow => Cadence is DerivedCadence.Weekly or DerivedCadence.Monthly;
}
