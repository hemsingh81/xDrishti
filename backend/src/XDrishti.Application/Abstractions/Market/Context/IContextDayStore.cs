using XDrishti.Domain.Market.Context;

namespace XDrishti.Application.Abstractions.Market.Context;

/// <summary>The stored days of the context (<c>market.context_daily</c>): insert-only, except that the six breadth columns of a day are set once (P3-01e).</summary>
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

    /// <summary>The breadth fields of every stored day, oldest first (<c>State</c> null while the day has none).</summary>
    ValueTask<IReadOnlyList<StoredBreadth>> ListBreadthAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Sets the breadth of days, once: only where the day has none, or (when the write allows it) where it is <c>insufficient</c>; a stored number is never touched. One transaction; returns how many days changed.
    /// </summary>
    ValueTask<int> SetBreadthAsync(IReadOnlyList<BreadthWrite> writes, CancellationToken cancellationToken);

    /// <summary>The explicit rebuild: replaces the breadth of the given days whatever it was (the only way a stored number changes). One transaction; returns how many days changed.</summary>
    ValueTask<int> ReplaceBreadthAsync(IReadOnlyList<BreadthWrite> writes, CancellationToken cancellationToken);
}

/// <summary>The number of stored days and the newest date, with the breadth written so far (how many days, when last, their sum): a cache key that also changes when a breadth is set or rebuilt.</summary>
public sealed record ContextStamp(int Count, DateOnly? Last, int BreadthDays = 0, DateTimeOffset? BreadthAt = null, double BreadthSum = 0);
