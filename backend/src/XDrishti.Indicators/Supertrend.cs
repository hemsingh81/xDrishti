using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators;

/// <summary><c>supertrend(n, mult)</c>: the Supertrend line, the final lower band while the trend is up and the final upper band while it is down (see <see cref="SupertrendCore"/>); ready at bar <c>n</c>.</summary>
public sealed class Supertrend : IIndicator
{
    private readonly int _period;

    private SupertrendCore _core;

    /// <param name="period">The ATR period.</param>
    /// <param name="multiplier">The band distance in ATRs; above 0 and finite.</param>
    public Supertrend(int period, double multiplier)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(period, 1);
        if (!double.IsFinite(multiplier) || multiplier <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(multiplier), multiplier, "The multiplier must be a finite number above 0.");
        }

        _period = period;
        _core = new SupertrendCore(period, multiplier);
    }

    public double Value => _core.Value;

    public bool IsReady => !double.IsNaN(_core.Value);

    public int WarmUp => _period;

    public void Update(in Bar bar) => _core.Update(bar);

    public void Reset() => _core.Clear();
}
