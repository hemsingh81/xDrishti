using XDrishti.Domain.Market.Context;

namespace XDrishti.Application.Abstractions.Market.Context;

/// <summary>The stored days of the context (<c>market.context_daily</c>), insert-only.</summary>
public interface IContextDayStore
{
    /// <summary>How many days are stored and the newest date: a cheap key for a cache of what was built from them.</summary>
    ValueTask<ContextStamp> StampAsync(CancellationToken cancellationToken);

    /// <summary>The dates of the stored days, oldest first (the notices need no prices).</summary>
    ValueTask<IReadOnlyList<DateOnly>> ListDatesAsync(CancellationToken cancellationToken);

    /// <summary>Every stored day, oldest first.</summary>
    ValueTask<IReadOnlyList<ContextDayRecord>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Stores the days that are not stored yet (<c>ON CONFLICT DO NOTHING</c>: a stored day is never touched); returns how many were new. <paramref name="indexInstrumentId"/> is the index the days came from.</summary>
    ValueTask<int> InsertAsync(Guid indexInstrumentId, IReadOnlyList<ContextDayRecord> records, CancellationToken cancellationToken);
}

/// <summary>The number of stored days and the newest date.</summary>
public sealed record ContextStamp(int Count, DateOnly? Last);
