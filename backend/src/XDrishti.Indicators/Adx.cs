using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators;

/// <summary><c>adx(n)</c>: Wilder's average directional index, seeded with the mean of the first <c>n</c> DX values and then smoothed (see <see cref="DirectionalMovement"/>); ready at bar <c>2n</c>.</summary>
public sealed class Adx : IIndicator
{
    private readonly int _period;

    private DirectionalMovement _core;

    public Adx(int period)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(period, 1);
        _period = period;
        _core = new DirectionalMovement(period);
    }

    public double Value => _core.Adx;

    public bool IsReady => !double.IsNaN(_core.Adx);

    public int WarmUp => 2 * _period;

    public void Update(in Bar bar) => _core.Update(bar);

    public void Reset() => _core.Clear();
}
