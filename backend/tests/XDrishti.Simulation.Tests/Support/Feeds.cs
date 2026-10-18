using XDrishti.Domain.Market.Bars;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>
/// The doctored inputs of the look-ahead property through the loop (spec P3-03a, criterion 14), shared by the scanner's property and the one through <c>Simulator.Run</c>: the replacement of everything after a cut by arbitrary values
/// and the <b>peeking feed</b>, the negative control built from the public API only.
/// </summary>
internal static class Feeds
{
    /// <summary>The bars after <paramref name="k"/> replaced by zero and huge prices and volumes, the epochs unchanged.</summary>
    public static Bar[] Replaced(Bar[] bars, int k, ulong seed)
    {
        var rng = new SeededSeries(seed ^ 0xABCDEF);
        var copy = (Bar[])bars.Clone();
        for (var i = k + 1; i < copy.Length; i++)
        {
            copy[i] = rng.Int(4) switch
            {
                0 => new Bar(copy[i].EpochSeconds, 0, 0, 0, 0, 0),
                1 => new Bar(copy[i].EpochSeconds, 1e12, 1e12, 1e12, 1e12, long.MaxValue / 4),
                2 => new Bar(copy[i].EpochSeconds, 1e12, 1e12, 0, 0.25, 0),
                _ => new Bar(copy[i].EpochSeconds, 0.25, 1e9, 0.25, 1e9, 1),
            };
        }

        return copy;
    }

    /// <summary>The identity: the bars as they are (the honest run).</summary>
    public static Bar[] Honest(Bar[] bars) => bars;

    /// <summary>The peeking feed: bar <i>i</i> gets the open, high, low, close and volume of bar <i>i</i>+1 with its own epoch; the last bar keeps its own.</summary>
    public static Bar[] Peeking(Bar[] bars)
    {
        var result = new Bar[bars.Length];
        for (var i = 0; i < result.Length; i++)
        {
            var source = i + 1 < bars.Length ? bars[i + 1] : bars[i];
            result[i] = new Bar(bars[i].EpochSeconds, source.Open, source.High, source.Low, source.Close, source.Volume);
        }

        return result;
    }

    /// <summary>The peeking walker input of P3-03 (AC8): every low becomes the lower of its own and the next minute's, every high the higher of its own and the next one's.</summary>
    public static Bar[] PeekingExtremes(Bar[] bars) => [.. LookAhead.Peek(bars)];
}
