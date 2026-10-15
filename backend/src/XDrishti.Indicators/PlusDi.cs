using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators;

/// <summary><c>plus_di(n)</c>: Wilder's positive directional indicator, 100·smoothed(+DM)/smoothed(TR) (see <see cref="DirectionalMovement"/>); ready at bar <c>n+1</c>.</summary>
public sealed class PlusDi : IIndicator
{
    private readonly int _period;

    private DirectionalMovement _core;

    public PlusDi(int period)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(period, 1);
        _period = period;
        _core = new DirectionalMovement(period);
    }

    public double Value => _core.PlusDi;

    public bool IsReady => !double.IsNaN(_core.PlusDi);

    public int WarmUp => _period + 1;

    public void Update(in Bar bar) => _core.Update(bar);

    public void Reset() => _core.Clear();
}
