using XDrishti.Domain.Baskets.Derived;

namespace XDrishti.Application.Market.Baskets;

/// <summary>The definition of a derived basket as the screen needs it, with the unit its score is printed in.</summary>
public static class DerivedScoreUnits
{
    /// <summary>The unit of the score of each basket: ATR%, mean absolute return, momentum and volatility are shares; traded value is rupees; the rest are ratios or counts.</summary>
    public static string Of(int definitionNo) => definitionNo switch
    {
        1 or 2 or 6 or 10 => "percent",
        3 => "rupees",
        _ => "ratio",
    };

    public static DerivedDefinitionInfo Info(int definitionNo)
    {
        var d = DerivedCatalogue.Get(definitionNo);
        return new DerivedDefinitionInfo(d.Id, d.Name, d.Description, d.NotThis, Of(d.Id));
    }
}
