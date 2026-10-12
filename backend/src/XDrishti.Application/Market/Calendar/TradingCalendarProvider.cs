using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Market.Calendar;

/// <summary>The scoped <see cref="ITradingCalendar"/>: reads the days once per request or job and keeps the pure calendar built from the confirmed ones.</summary>
internal sealed class TradingCalendarProvider(ITradingDayStore store) : ITradingCalendar
{
    private TradingCalendar? _calendar;

    public async ValueTask<TradingCalendar> GetAsync(CancellationToken cancellationToken) =>
        _calendar ??= new TradingCalendar(await store.ListAsync(cancellationToken));

    public void Invalidate() => _calendar = null;
}
