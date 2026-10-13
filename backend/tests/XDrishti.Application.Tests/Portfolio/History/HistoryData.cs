using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Returns;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Tests.Portfolio.History;

/// <summary>
/// Builders for the history tests. The days are October 2026 (made-up prices): Mon 5, Tue 6, Wed 7, Thu 8, Fri 9, Sat 10, Sun 11, Mon 12, ... Fri 30; November: Mon 2 ... Fri 6.
/// </summary>
internal static class HistoryData
{
    public static readonly TradingCalendar Weekdays = new();

    public static DateOnly Oct(int day) => new(2026, 10, day);

    public static readonly Guid Alpha = Guid.Parse("00000000-0000-7000-8000-00000000a001");

    public static readonly Guid Beta = Guid.Parse("00000000-0000-7000-8000-00000000b002");

    public static DailyClose[] Closes(params (int Day, decimal Close)[] closes) => [.. closes.Select(c => new DailyClose(Oct(c.Day), c.Close))];

    public static LotData Lot(long qty, decimal cost, int acquiredDay) => HoldingFixtures.Open(qty, cost, Oct(acquiredDay));

    public static LotData Sold(long qty, decimal cost, int acquiredDay, int closedDay, decimal price) => HoldingFixtures.Closed(qty, cost, Oct(acquiredDay), Oct(closedDay), price);

    /// <summary>An open holding of the instrument (the price is the broker's last price, not used by the history).</summary>
    public static HoldingData Holding(Guid? instrument, params LotData[] lots) => HoldingFixtures.Holding(100m, "SYM", "open", instrument, lots);

    public static HistoryInput Input(
        IEnumerable<HoldingData> holdings,
        IReadOnlyDictionary<Guid, IReadOnlyList<DailyClose>> closes,
        IReadOnlyList<DailyClose>? benchmark = null,
        TradingCalendar? calendar = null,
        LivePrices? live = null) =>
        new([.. holdings], closes, benchmark ?? [], calendar ?? Weekdays, live);

    public static Dictionary<Guid, IReadOnlyList<DailyClose>> By(params (Guid Instrument, DailyClose[] Closes)[] closes) =>
        closes.ToDictionary(c => c.Instrument, c => (IReadOnlyList<DailyClose>)c.Closes);

    public static TradingCalendar WithHoliday(params DateOnly[] dates) => new(dates.Select(d => TradingDay.Holiday(d, CalendarSource.Manual, CalendarStatus.Confirmed, null, new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero))));
}
