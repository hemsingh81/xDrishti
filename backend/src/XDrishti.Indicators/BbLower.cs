using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators;

/// <summary><c>bb_lower(n, k)</c>: <c>middle − k·σ</c> with σ the population standard deviation of the last <c>n</c> values (<see cref="BollingerCore"/>); ready at bar <c>n</c>.</summary>
public sealed class BbLower : IIndicator
{
    private readonly int _period;

    private readonly double _k;

    private readonly PriceSource _source;

    private BollingerCore _core;

    private double _value = double.NaN;

    /// <param name="period">The number of values in the window.</param>
    /// <param name="k">The band width in standard deviations; above 0 and finite.</param>
    /// <param name="source">The price read (default close).</param>
    public BbLower(int period, double k, PriceSource source = PriceSource.Close)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(period, 1);
        if (!double.IsFinite(k) || k <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(k), k, "k must be a finite number above 0.");
        }

        _period = period;
        _k = k;
        _source = source;
        _core = new BollingerCore(period, needSigma: true);
    }

    public double Value => _value;

    public bool IsReady => !double.IsNaN(_value);

    public int WarmUp => _period;

    public void Update(in Bar bar)
    {
        _core.Add(PriceSources.Pick(_source, bar));
        if (!double.IsNaN(_core.Middle))
        {
            _value = _core.Middle - _k * _core.Sigma;
        }
    }

    public void Reset()
    {
        _core.Clear();
        _value = double.NaN;
    }
}
