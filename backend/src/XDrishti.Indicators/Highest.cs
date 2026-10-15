using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators;

/// <summary><c>highest(n, source)</c>: the maximum of the last <c>n</c> values of the source including the current bar (default high); ready at bar <c>n</c>. A monotone deque: amortised O(1) per bar.</summary>
public sealed class Highest : IIndicator
{
    private readonly int _period;

    private readonly PriceSource _source;

    private MonotoneWindow _window;

    public Highest(int period, PriceSource source = PriceSource.High)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(period, 1);
        _period = period;
        _source = source;
        _window = new MonotoneWindow(period, maximum: true);
    }

    public double Value => IsReady ? _window.Extreme : double.NaN;

    public bool IsReady => _window.Fed >= _period;

    public int WarmUp => _period;

    public void Update(in Bar bar) => _window.Add(PriceSources.Pick(_source, bar));

    public void Reset() => _window.Clear();
}
