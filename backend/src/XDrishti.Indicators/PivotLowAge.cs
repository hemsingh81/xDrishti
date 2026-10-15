using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators;

/// <summary><c>pivot_low_age(n)</c>: the number of bars since the pivot that <c>pivot_low(n)</c> reports (n at the bar that confirmed it, growing by one per bar); NaN until the first pivot. For structure stops (<see cref="PivotCore"/>).</summary>
public sealed class PivotLowAge : IIndicator
{
    private readonly int _period;

    private PivotCore _core;

    /// <param name="period">How many bars on each side the swing point must beat; at least 1.</param>
    public PivotLowAge(int period)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(period, 1);
        _period = period;
        _core = new PivotCore(period, high: false);
    }

    public double Value => _core.Age;

    public bool IsReady => !double.IsNaN(_core.Age);

    /// <summary>The earliest bar at which a pivot can be confirmed, <c>2n + 1</c>; the feature is ready from the first pivot on, which depends on the data.</summary>
    public int WarmUp => 2 * _period + 1;

    public void Update(in Bar bar) => _core.Update(bar);

    public void Reset() => _core.Clear();
}
