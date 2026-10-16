using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators;

/// <summary><c>lowest(n, source)</c>: the minimum of the last <c>n</c> values of the source including the current bar (default low); ready at bar <c>n</c>. A monotone deque: amortised O(1) per bar.</summary>
public sealed class Lowest : IIndicator
{
    private readonly int _period;

    private readonly PriceSource _source;

    private MonotoneWindow _window;

    public Lowest(int period, PriceSource source = PriceSource.Low)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(period, 1);
        _period = period;
        _source = source;
        _window = new MonotoneWindow(period, maximum: false);
    }

    public double Value => IsReady ? _window.Extreme : double.NaN;

    public bool IsReady => _window.Fed >= _period;

    public int WarmUp => _period;

    public void Update(in Bar bar) => _window.Add(PriceSources.Pick(_source, bar));

    public void Reset() => _window.Clear();
}
