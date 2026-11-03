using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Options.Iv;
using XDrishti.Domain.Options.Remark;

namespace XDrishti.Domain.Tests.Options.Iv;

/// <summary>Builders for the iv-1 tests: a Wednesday before a Tuesday weekly expiry, bars on a five-minute grid, and a NIFTY plan.</summary>
internal static class IvKit
{
    public static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    public static readonly DateOnly Wed = new(2026, 7, 29);

    public static readonly TradingCalendar Calendar = new();

    public static DateTimeOffset At(int hour, int minute, DateOnly? date = null) => new((date ?? Wed).ToDateTime(new TimeOnly(hour, minute)), Ist);

    public static OptionBar Bar(DateTimeOffset ts, int offset, float? iv, long? oi = 100, bool usable = true, bool suspect = false) =>
        new(ts, offset, 2_400_000, 2_400_000, 100, 100, 100, 100, 10, oi, iv, suspect, usable);

    public static ExpiryCalendar Expiries(string symbol = ExpiryRules.Nifty, params (DateOnly Date, char Flag)[] contracts) =>
        new(symbol, contracts.Select(c => new ContractExpiry(c.Date, c.Flag)), [], Calendar);

    public static ExpiryCalendar NiftyWeeklies() => Expiries(ExpiryRules.Nifty, (new DateOnly(2026, 8, 4), 'W'), (new DateOnly(2026, 8, 11), 'W'), (new DateOnly(2026, 8, 18), 'W'));

    public static IvSeriesPlan Plan(int front = 1, int? back = 2, DateOnly? expiry = null) => new('W', front, expiry ?? new DateOnly(2026, 8, 4), back, 5);

    /// <summary>Adds the CE and PE offset-0 bar at each start time with the given IVs, plus seven-offset open interest of the given call and put totals spread over the offsets.</summary>
    public static IvBarBook Book(int code, DateTimeOffset start, float? ceIv, float? peIv, long? ceOiPerOffset = 100, long? peOiPerOffset = 100, bool usable = true)
    {
        var book = new IvBarBook();
        book.Add(code, "CE", 5, Offsets(start, ceIv, ceOiPerOffset, usable));
        book.Add(code, "PE", 5, Offsets(start, peIv, peOiPerOffset, usable));
        return book;
    }

    public static IEnumerable<OptionBar> Offsets(DateTimeOffset start, float? iv, long? oi, bool usable = true)
    {
        for (var offset = -3; offset <= 3; offset++)
        {
            yield return Bar(start, offset, offset == 0 ? iv : 12f, oi, usable);
        }
    }

    public static IReadOnlyDictionary<DateOnly, double> Series(DateOnly end, int sessions, Func<int, double> value)
    {
        var map = new Dictionary<DateOnly, double>();
        var date = end;
        for (var i = 1; i <= sessions; i++)
        {
            do
            {
                date = date.AddDays(-1);
            }
            while (!Calendar.IsTradingDay(date));
            map[date] = value(i);
        }

        return map;
    }

    public static IvFeatureInput Input(
        DateTimeOffset? at = null,
        IvSeriesPlan? plan = null,
        IvBarBook? bars = null,
        IReadOnlyDictionary<DateOnly, double>? own = null,
        IReadOnlyDictionary<DateOnly, double>? nifty = null,
        IReadOnlyDictionary<DateOnly, double>? closes = null,
        IReadOnlyList<XDrishti.Domain.Filings.CalendarRow>? rows = null) =>
        new(at ?? At(11, 0), Calendar, plan, bars, own, nifty, closes ?? new Dictionary<DateOnly, double>(), [], rows ?? [], TimeSpan.Zero);
}
