using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators;

/// <summary>
/// <c>ema(n)</c>: <c>α = 2/(n+1)</c>, seeded with the simple mean of the first <c>n</c> values (ready at bar <c>n</c>), then <c>ema = α·x + (1−α)·ema</c>. The same seeding as Skender.Stock.Indicators.
/// </summary>
public sealed class Ema : IIndicator
{
    private readonly int _period;

    private readonly PriceSource _source;

    private EmaCore _core;

    public Ema(int period, PriceSource source = PriceSource.Close)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(period, 1);
        _period = period;
        _source = source;
        _core = new EmaCore(period);
    }

    public double Value => _core.Value;

    public bool IsReady => _core.IsReady;

    public int WarmUp => _period;

    public void Update(in Bar bar) => _core.Add(PriceSources.Pick(_source, bar));

    public void Reset() => _core.Clear();
}
