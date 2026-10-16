using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators.Tests.Support;

/// <summary>Feeds bars to an indicator and returns the value after each one (NaN while it is not ready).</summary>
internal static class HandRun
{
    public static readonly DateOnly Day = new(2026, 3, 2);

    public static List<double> Run(IIndicator indicator, IEnumerable<Bar> bars)
    {
        var values = new List<double>();
        foreach (var bar in bars)
        {
            indicator.Update(bar);
            values.Add(indicator.Value);
        }

        return values;
    }

    /// <summary>Bars at consecutive minutes from the open: (open, high, low, close) per row.</summary>
    public static List<Bar> Candles(params (double Open, double High, double Low, double Close)[] rows) =>
        [.. rows.Select((r, i) => Minutes.Bar(Day, Minutes.Open + i, r.Open, r.High, r.Low, r.Close))];

    /// <summary>Bars with only a high and a low (open and close in the middle), for the range and swing examples.</summary>
    public static List<Bar> Ranges(params (double High, double Low)[] rows) =>
        [.. rows.Select((r, i) => Minutes.Bar(Day, Minutes.Open + i, (r.High + r.Low) / 2, r.High, r.Low, (r.High + r.Low) / 2))];
}
