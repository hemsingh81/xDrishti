using XDrishti.Domain.Learning;

namespace XDrishti.Application.Abstractions.Learning;

/// <summary>
/// The daily traded values of the tracked shares (BK-11, ADR 0261): all the tier tagger reads. A read, never a write.
/// </summary>
public interface IUniverseSeriesSource
{
    /// <summary>
    /// For each tracked share, its sessions (India date, close x volume in rupees) from <paramref name="from"/> up to, not including, <paramref name="toExclusive"/>, oldest first. A share without a session in the range is absent. Nothing at or after
    /// <paramref name="toExclusive"/> is returned, so a caller that passes the day of its latest decision cannot read that day's bar.
    /// </summary>
    ValueTask<IReadOnlyDictionary<Guid, IReadOnlyList<SessionValue>>> ReadAsync(DateOnly from, DateOnly toExclusive, CancellationToken cancellationToken);
}
