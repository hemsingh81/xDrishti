using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Domain.Options.Iv;

/// <summary>
/// The rank and the percentile of the previous session's daily ATM IV among the 252 sessions before the trade date (P14-11, ADR 0199, pure). A rank that included the trade date's own close would read the future for an intraday decision, so the window ends the session before
/// and nothing of the trade date or later is read. Fewer than <see cref="MinValues"/> values is <see cref="FeatureReasons.ShortHistory"/> (so is a missing value for the previous session), a flat window is <see cref="IvReasons.FlatHistory"/>.
/// </summary>
public static class IvRank
{
    public const int Window = 252;

    public const int MinValues = 200;

    private const int LookbackDays = 700;

    /// <summary>The rank <c>(x - min) / (max - min)</c> and the share of the values below <paramref name="x"/>.</summary>
    public static IvRankResult Of(IReadOnlyList<double> history, double x)
    {
        ArgumentNullException.ThrowIfNull(history);
        if (history.Count < MinValues || !double.IsFinite(x))
        {
            return Unknown(FeatureReasons.ShortHistory);
        }

        var min = history.Min();
        var max = history.Max();
        if (max <= min)
        {
            return Unknown(IvReasons.FlatHistory);
        }

        var below = history.Count(v => v < x);
        return new IvRankResult((x - min) / (max - min), below / (double)history.Count, null);
    }

    /// <summary>The rank of the previous session's value among the values of the 252 sessions before <paramref name="tradeDate"/>.</summary>
    public static IvRankResult OfSeries(IReadOnlyDictionary<DateOnly, double> series, DateOnly tradeDate, TradingCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(calendar);
        var values = new List<double>(Window);
        double? previous = null;
        var sessions = 0;
        for (var back = 1; back <= LookbackDays && sessions < Window; back++)
        {
            var date = tradeDate.AddDays(-back);
            if (!calendar.IsTradingDay(date))
            {
                continue;
            }

            sessions++;
            if (series.TryGetValue(date, out var value))
            {
                values.Add(value);
                if (sessions == 1)
                {
                    previous = value;
                }
            }
        }

        return previous is { } x ? Of(values, x) : Unknown(FeatureReasons.ShortHistory);
    }

    private static IvRankResult Unknown(string reason) => new(double.NaN, double.NaN, reason);
}
