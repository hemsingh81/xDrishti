using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Abstractions.Market.Calendar;

/// <summary>
/// The trading calendar for any slice that needs to know what a session is (freshness, gaps, the end-of-day schedule, quality). Scoped: the confirmed days are
/// read once and the in-memory <see cref="TradingCalendar"/> is kept for the whole request or job, so everything in it sees the same calendar.
/// </summary>
public interface ITradingCalendar
{
    /// <summary>The calendar of the confirmed days (read on first use, then kept until <see cref="Invalidate"/>).</summary>
    ValueTask<TradingCalendar> GetAsync(CancellationToken cancellationToken);

    /// <summary>Forgets the kept calendar after something in this scope changed the days; the next <see cref="GetAsync"/> reads again.</summary>
    void Invalidate();
}
