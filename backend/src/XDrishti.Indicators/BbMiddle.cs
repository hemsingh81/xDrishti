using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators;

/// <summary><c>bb_middle(n, k)</c>: the middle Bollinger band, the mean of the last <c>n</c> values of the source (<see cref="BollingerCore"/>); ready at bar <c>n</c>.</summary>
public sealed class BbMiddle : IIndicator
{
    private readonly int _period;

    private readonly double _k;

    private readonly PriceSource _source;

    private BollingerCore _core;

    private double _value = double.NaN;

    /// <param name="period">The number of values in the window.</param>
    /// <param name="k">The band width in standard deviations; above 0 and finite.</param>
    /// <param name="source">The price read (default close).</param>
    public BbMiddle(int period, double k, PriceSource source = PriceSource.Close)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(period, 1);
        if (!double.IsFinite(k) || k <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(k), k, "k must be a finite number above 0.");
        }

        _period = period;
        _k = k;
        _source = source;
        _core = new BollingerCore(period, needSigma: false);
    }

    public double Value => _value;

    public bool IsReady => !double.IsNaN(_value);

    public int WarmUp => _period;

    public void Update(in Bar bar)
    {
        _core.Add(PriceSources.Pick(_source, bar));
        if (!double.IsNaN(_core.Middle))
        {
            _value = _core.Middle;
        }
    }

    public void Reset()
    {
        _core.Clear();
        _value = double.NaN;
    }
}
