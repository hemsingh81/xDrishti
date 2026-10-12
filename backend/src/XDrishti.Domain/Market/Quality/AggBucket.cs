using XDrishti.Domain.Market.Bars;

namespace XDrishti.Domain.Market.Quality;

/// <summary>A derived bar as recomputed from the 1-minute bars: the values the stored aggregate must have. <see cref="Bars"/> counts the 1-minute bars in it.</summary>
public readonly record struct AggBucket(double Open, double High, double Low, double Close, long Volume, int Bars);

/// <summary>A derived bar as stored in <c>market.bars_agg</c> (<see cref="Start"/> is the bucket start as epoch seconds).</summary>
public readonly record struct StoredBucket(Timeframe Timeframe, long Start, double Open, double High, double Low, double Close, long Volume, int Bars)
{
    public AggBucket Values => new(Open, High, Low, Close, Volume, Bars);
}
