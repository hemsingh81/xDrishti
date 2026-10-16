using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators;

/// <summary><c>minus_di(n)</c>: Wilder's negative directional indicator, 100·smoothed(−DM)/smoothed(TR) (see <see cref="DirectionalMovement"/>); ready at bar <c>n+1</c>.</summary>
public sealed class MinusDi : IIndicator
{
    private readonly int _period;

    private DirectionalMovement _core;

    public MinusDi(int period)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(period, 1);
        _period = period;
        _core = new DirectionalMovement(period);
    }

    public double Value => _core.MinusDi;

    public bool IsReady => !double.IsNaN(_core.MinusDi);

    public int WarmUp => _period + 1;

    public void Update(in Bar bar) => _core.Update(bar);

    public void Reset() => _core.Clear();
}
