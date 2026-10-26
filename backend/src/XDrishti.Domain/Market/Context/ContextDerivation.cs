using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Domain.Market.Context;

/// <summary>
/// Pure: the daily rows of the regime index and of the volatility index to the records of <c>market.context_daily</c> (P3-01d, ADR 0033). A day is written only when it is final (at or before the last closed session, D9) and its index leg has
/// at least <see cref="ContextDerivationOptions.MinDayFraction"/> of the calendar's expected minutes; its VIX leg is taken when it is as full, else the day waits for it until it is <see cref="ContextDerivationOptions.VixGraceSessions"/> sessions old
/// and then goes without (D10: the table is insert-only, so a late VIX must not be lost and a restated one cannot be written). Dates that are stored already are never derived again. The input may be unordered and may repeat a date (the last row wins).
/// </summary>
public static class ContextDerivation
{
    public static ContextDerivationResult Derive(
        IEnumerable<DailyLeg> indexLegs,
        IEnumerable<DailyLeg> vixLegs,
        TradingCalendar calendar,
        DateOnly lastClosedSession,
        IReadOnlySet<DateOnly> existingDates,
        ContextDerivationOptions options)
    {
        ArgumentNullException.ThrowIfNull(indexLegs);
        ArgumentNullException.ThrowIfNull(vixLegs);
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(existingDates);
        ArgumentNullException.ThrowIfNull(options);

        var index = Latest(indexLegs);
        var vix = Latest(vixLegs);
        var records = new List<ContextDayRecord>();
        var pending = new List<DateOnly>();
        var skipped = new List<SkippedContextDay>();
        if (index.Count == 0)
        {
            return new ContextDerivationResult(records, pending, skipped);
        }

        var first = index.Keys.Min();
        var dates = index.Keys.Union(vix.Keys.Where(d => d >= first)).Where(d => d <= lastClosedSession).ToHashSet();
        for (var date = first; date <= lastClosedSession; date = date.AddDays(1))
        {
            if (calendar.IsTradingDay(date))
            {
                dates.Add(date);
            }
        }

        foreach (var date in dates.Order())
        {
            if (existingDates.Contains(date))
            {
                continue;
            }

            if (!calendar.IsTradingDay(date))
            {
                skipped.Add(new SkippedContextDay(date, ContextSkipReason.NotASession, index.TryGetValue(date, out var stray) ? stray.Bars : 0));
                continue;
            }

            var expected = calendar.ExpectedMinutes(date);
            if (!index.TryGetValue(date, out var indexLeg))
            {
                skipped.Add(new SkippedContextDay(date, ContextSkipReason.IndexMissing, 0));
                continue;
            }

            if (!IsFull(indexLeg, expected, options))
            {
                skipped.Add(new SkippedContextDay(date, ContextSkipReason.IndexThin, indexLeg.Bars));
                continue;
            }

            if (!IsPossible(indexLeg))
            {
                skipped.Add(new SkippedContextDay(date, ContextSkipReason.IndexInvalid, indexLeg.Bars));
                continue;
            }

            DailyLeg? accepted = null;
            if (vix.TryGetValue(date, out var vixLeg))
            {
                if (!IsFull(vixLeg, expected, options))
                {
                    skipped.Add(new SkippedContextDay(date, ContextSkipReason.VixThin, vixLeg.Bars));
                }
                else if (double.IsFinite(vixLeg.Close) && vixLeg.Close > 0)
                {
                    accepted = vixLeg;
                }
            }

            if (accepted is null && calendar.TradingDaysBetween(date, lastClosedSession) < options.VixGraceSessions)
            {
                pending.Add(date);
                continue;
            }

            records.Add(new ContextDayRecord(date, indexLeg.High, indexLeg.Low, indexLeg.Close, accepted?.Close, indexLeg.Bars, accepted?.Bars));
        }

        return new ContextDerivationResult(records, pending, skipped);
    }

    private static Dictionary<DateOnly, DailyLeg> Latest(IEnumerable<DailyLeg> legs)
    {
        var map = new Dictionary<DateOnly, DailyLeg>();
        foreach (var leg in legs)
        {
            map[leg.Date] = leg;
        }

        return map;
    }

    private static bool IsFull(DailyLeg leg, int expectedMinutes, ContextDerivationOptions options) =>
        expectedMinutes > 0 && (double)leg.Bars / expectedMinutes >= options.MinDayFraction;

    private static bool IsPossible(DailyLeg leg) =>
        double.IsFinite(leg.High) && double.IsFinite(leg.Low) && double.IsFinite(leg.Close)
        && leg.Low > 0 && leg.High >= leg.Low && leg.Close >= leg.Low && leg.Close <= leg.High;
}
