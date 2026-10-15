using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators;

/// <summary><c>inside_bar</c>: 1 when the bar's high is below the previous bar's high and its low is above the previous low (strictly inside), otherwise 0; ready at bar 2.</summary>
public sealed class InsideBar : IIndicator
{
    private double _high;

    private double _low;

    private bool _seen;

    private double _value = double.NaN;

    public double Value => _value;

    public bool IsReady => !double.IsNaN(_value);

    public int WarmUp => 2;

    public void Update(in Bar bar)
    {
        if (_seen)
        {
            _value = bar.High < _high && bar.Low > _low ? 1 : 0;
        }

        _seen = true;
        _high = bar.High;
        _low = bar.Low;
    }

    public void Reset()
    {
        _seen = false;
        _high = 0;
        _low = 0;
        _value = double.NaN;
    }
}
