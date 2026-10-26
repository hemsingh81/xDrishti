using XDrishti.Application.Abstractions.Live;

namespace XDrishti.Application.Abstractions.Depth;

/// <summary>A tracked share with its mean daily turnover (close x volume) over its last sessions.</summary>
public sealed record DepthTurnover(InstrumentSecurity Security, double MeanTurnover);

/// <summary>The read-only inputs of the day's recording set that are not armed tickets (P7-16): the most liquid tracked shares. Reads bars only.</summary>
public interface IDepthUniverse
{
    /// <summary>Tracked, live shares with a Dhan NSE equity id, most liquid first (mean turnover of the last 20 daily sessions before <paramref name="before"/>; ties by symbol), at most <paramref name="take"/>.</summary>
    ValueTask<IReadOnlyList<DepthTurnover>> MostLiquidAsync(DateOnly before, int take, CancellationToken cancellationToken);
}
