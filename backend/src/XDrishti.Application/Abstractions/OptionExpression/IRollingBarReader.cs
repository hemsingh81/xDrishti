using XDrishti.Domain.Options.Iv;
using XDrishti.Domain.Options.Remark;

namespace XDrishti.Application.Abstractions.OptionExpression;

/// <summary>
/// The bounded read of the backfilled rolling bars for the option re-mark (P14-08, ADR 0197, ADR 0177 consequence): the bars of <b>one</b> series (underlying, kind, code, side, bar width) over a time range, all offsets, plus the archive watermark of that series and range.
/// A range longer than <see cref="MaxRangeDays"/> is refused. It returns prices as stored (paise); it sets no usability (the use case does, from the stored quality verdicts) and writes nothing. The third reader of <c>options.rolling_bars</c>, after the quality reader.
/// </summary>
public interface IRollingBarReader
{
    /// <summary>The longest range one call reads.</summary>
    public const int MaxRangeDays = 366;

    /// <summary>The bars of the series with <paramref name="from"/> &lt;= ts &lt; <paramref name="to"/>, oldest first; every bar has <see cref="OptionBar.Usable"/> false.</summary>
    ValueTask<IReadOnlyList<OptionBar>> BarsAsync(Guid underlyingId, char expiryFlag, int expiryCode, string side, int intervalMinutes, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    /// <summary>The bars of the series with <paramref name="from"/> &lt;= ts &lt; <paramref name="to"/> at the given offsets only (filtered in the query), oldest first; every bar has <see cref="OptionBar.Usable"/> false (P14-11).</summary>
    ValueTask<IReadOnlyList<OptionBar>> BarsAsync(Guid underlyingId, char expiryFlag, int expiryCode, string side, int intervalMinutes, IReadOnlyCollection<int> offsets, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    /// <summary>The longest range one <see cref="DailyLastBarsAsync"/> call reads.</summary>
    public const int MaxDailyRangeDays = 2_300;

    /// <summary>For each India session in the range, each code and each side, the offset-0 IV of the last bar of the session (null when that bar carries none); <see cref="DailySideIv.Usable"/> is false (P14-11). One grouped query over the range.</summary>
    ValueTask<IReadOnlyList<DailySideIv>> DailyLastBarsAsync(Guid underlyingId, char expiryFlag, IReadOnlyCollection<int> expiryCodes, int intervalMinutes, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    /// <summary>Whether the underlying has any stored bar at all: an existence query, never a list (P14-11).</summary>
    ValueTask<bool> HasSeriesAsync(Guid underlyingId, CancellationToken cancellationToken);

    /// <summary>The highest <c>options.fetch_log.id</c> of a finished fetch of the underlying whose window overlaps the range; 0 when there is none.</summary>
    ValueTask<long> WatermarkAsync(Guid underlyingId, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
