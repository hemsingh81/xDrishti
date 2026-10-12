using XDrishti.Domain.Market.Quality;

namespace XDrishti.Application.Abstractions.Market.Quality;

/// <summary>An instrument the quality checks look at: tracked, not retired, with 1-minute bars. <see cref="FirstBar"/> and <see cref="LastBar"/> are its oldest and newest stored bars.</summary>
public sealed record QualityTarget(Guid InstrumentId, string Symbol, DateTimeOffset FirstBar, DateTimeOffset LastBar);

/// <summary>What the quality checks read besides the 1-minute bars themselves (<see cref="Abstractions.Market.Bars.IBarStore.ReadAsync"/> gives those).</summary>
public interface IQualityData
{
    /// <summary>The instruments to check (tracked, not retired, with bars), by symbol; only the one when <paramref name="instrumentId"/> is given and qualifies.</summary>
    ValueTask<IReadOnlyList<QualityTarget>> ListTargetsAsync(Guid? instrumentId, CancellationToken cancellationToken);

    /// <summary>The stored derived bars of the instrument from the bounds on (intraday and day buckets from the window, week buckets from the week start, month buckets from the month start), all timeframes, by the primary key.</summary>
    ValueTask<IReadOnlyList<StoredBucket>> ReadStoredBucketsAsync(Guid instrumentId, QualityBounds bounds, CancellationToken cancellationToken);
}
