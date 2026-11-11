namespace XDrishti.Application.Abstractions.OpeningBars;

/// <summary>The write side of <c>market.bars_1m_live</c> and the day's events (BK-03b): insert-only for the runtime role, a second write of a minute is left alone.</summary>
public interface IOpeningBarsStore
{
    /// <summary>Writes the bars in one statement, <c>ON CONFLICT DO NOTHING</c>. Returns how many were new (the rest are duplicates).</summary>
    ValueTask<int> AppendAsync(IReadOnlyList<MinuteBar> bars, CancellationToken cancellationToken);

    ValueTask AddEventAsync(DateOnly day, OpeningBarsEvent @event, CancellationToken cancellationToken);

    /// <summary>The free bytes of the file system this process runs on; null when they cannot be read.</summary>
    ValueTask<long?> FreeBytesAsync(CancellationToken cancellationToken);
}
