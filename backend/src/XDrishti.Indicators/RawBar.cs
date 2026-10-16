using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators;

/// <summary>
/// The raw bar features <c>open</c>, <c>high</c>, <c>low</c>, <c>close</c> and <c>volume</c>: the value of the last <b>closed</b> bar of the timeframe (so <c>tf("1D").close</c> is yesterday's close until today's daily bar
/// has closed). Ready after the first bar. The frame's value history turns them into <c>close[1]</c> and the like.
/// </summary>
public sealed class RawBar : IIndicator
{
    private readonly PriceSource _source;

    private double _value = double.NaN;

    /// <param name="source">Which value of the bar: open, high, low, close or <see cref="PriceSource.Volume"/>.</param>
    public RawBar(PriceSource source)
    {
        if (source is not (PriceSource.Open or PriceSource.High or PriceSource.Low or PriceSource.Close or PriceSource.Volume))
        {
            throw new ArgumentOutOfRangeException(nameof(source), source, "A raw bar feature reads open, high, low, close or volume.");
        }

        _source = source;
    }

    public double Value => _value;

    public bool IsReady => !double.IsNaN(_value);

    public int WarmUp => 1;

    public void Update(in Bar bar) => _value = PriceSources.Pick(_source, bar);

    public void Reset() => _value = double.NaN;
}
