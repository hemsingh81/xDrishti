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

    /// <summary>The highest <c>options.fetch_log.id</c> of a finished fetch of the underlying whose window overlaps the range; 0 when there is none.</summary>
    ValueTask<long> WatermarkAsync(Guid underlyingId, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
