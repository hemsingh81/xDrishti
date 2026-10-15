using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators;

/// <summary>
/// One feature the frame is asked for (ADR 0031): an indicator <see cref="Name"/> (see <see cref="IndicatorCatalog"/>), the <see cref="Timeframe"/> of the bars it reads, the <see cref="Source"/> price (ignored by
/// indicators that read the whole bar) and up to four numeric parameters in the order the catalog lists them (unused ones are 0). A plain value: two equal specs ask for the same number.
/// </summary>
public readonly record struct FeatureSpec(
    string Name,
    Timeframe Timeframe = Timeframe.M1,
    PriceSource Source = PriceSource.Close,
    double P1 = 0,
    double P2 = 0,
    double P3 = 0,
    double P4 = 0)
{
    /// <summary>The parameter at the position (0 to 3).</summary>
    public double Parameter(int index) => index switch
    {
        0 => P1,
        1 => P2,
        2 => P3,
        3 => P4,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };
}
