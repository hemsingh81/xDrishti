using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators;

/// <summary><c>macd_hist(fast, slow, signal)</c>: <c>macd_line − macd_signal</c> (see <see cref="MacdCore"/>); ready at bar <c>slow + signal − 1</c>.</summary>
public sealed class MacdHist : IIndicator
{
    private readonly int _slow;

    private readonly int _signalPeriod;

    private readonly PriceSource _source;

    private MacdCore _core;

    /// <param name="fast">The fast EMA period; must be below <paramref name="slow"/>.</param>
    /// <param name="slow">The slow EMA period.</param>
    /// <param name="signal">The signal EMA period.</param>
    /// <param name="source">The price read (default close).</param>
    public MacdHist(int fast, int slow, int signal, PriceSource source = PriceSource.Close)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(fast, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(slow, fast);
        ArgumentOutOfRangeException.ThrowIfLessThan(signal, 1);
        _slow = slow;
        _signalPeriod = signal;
        _source = source;
        _core = new MacdCore(fast, slow, signal);
    }

    public double Value => _core.Histogram;

    public bool IsReady => !double.IsNaN(_core.Histogram);

    public int WarmUp => _slow + _signalPeriod - 1;

    public void Update(in Bar bar) => _core.Add(PriceSources.Pick(_source, bar));

    public void Reset() => _core.Clear();
}
