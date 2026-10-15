using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators;

/// <summary><c>pivot_high(n)</c>: the high of the most recent confirmed swing high, a bar whose high is strictly above the highs of the <c>n</c> bars before and after it (<see cref="PivotCore"/>). Confirmed when the n-th later bar closes; NaN until the first pivot.</summary>
public sealed class PivotHigh : IIndicator
{
    private readonly int _period;

    private PivotCore _core;

    /// <param name="period">How many bars on each side the swing point must beat; at least 1.</param>
    public PivotHigh(int period)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(period, 1);
        _period = period;
        _core = new PivotCore(period, high: true);
    }

    public double Value => _core.Value;

    public bool IsReady => !double.IsNaN(_core.Value);

    /// <summary>The earliest bar at which a pivot can be confirmed, <c>2n + 1</c>; the feature is ready from the first pivot on, which depends on the data.</summary>
    public int WarmUp => 2 * _period + 1;

    public void Update(in Bar bar) => _core.Update(bar);

    public void Reset() => _core.Clear();
}
