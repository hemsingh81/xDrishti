using XDrishti.Domain.Baskets.Derived;

namespace XDrishti.Application.Market.Baskets;

/// <summary>The definition of a derived basket as the screen needs it.</summary>
public static class DerivedScoreUnits
{
    public static DerivedDefinitionInfo Info(int definitionNo)
    {
        var d = DerivedCatalogue.Get(definitionNo);
        return new DerivedDefinitionInfo(d.Id, d.Name, d.Description, d.NotThis, d.Unit);
    }
}
