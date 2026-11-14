namespace XDrishti.SimReader;

/// <summary>Plain averages over closed bars, written from the textbook definitions.</summary>
internal static class ReaderMath
{
    /// <summary>The exponential average of the last value, seeded with the simple average of the first <paramref name="period"/> values; null until there are that many.</summary>
    public static decimal? Ema(IReadOnlyList<decimal> values, int period)
    {
        if (values.Count < period)
        {
            return null;
        }

        var ema = values.Take(period).Average();
        var k = 2m / (period + 1);
        for (var i = period; i < values.Count; i++)
        {
            ema = ((values[i] - ema) * k) + ema;
        }

        return ema;
    }

    /// <summary>The simple average of the last <paramref name="period"/> values; null until there are that many.</summary>
    public static decimal? Sma(IReadOnlyList<decimal> values, int period) => values.Count < period ? null : values.Skip(values.Count - period).Average();
}
