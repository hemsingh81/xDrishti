using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Domain.Options.Iv;

/// <summary>
/// The annualised standard deviation (sample, n - 1, times the square root of 252) of the 20 daily log returns of the 21 sessions before the trade date (P14-11, pure). Closes are the raw ones; a split or bonus inside the window is put on one basis with the factor of the actions
/// ex-dated after the earlier close and up to the later one. A close missing for any of the 21 sessions is <see cref="FeatureReasons.ShortHistory"/>; nothing of the trade date or later is read.
/// </summary>
public static class RealisedVolatility
{
    public const int Returns = 20;

    private const int LookbackDays = 60;

    public static FeatureCell Annualised(IReadOnlyDictionary<DateOnly, double> closes, DateOnly tradeDate, TradingCalendar calendar, IReadOnlyList<AppliedAction> actions)
    {
        ArgumentNullException.ThrowIfNull(closes);
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(actions);
        var sessions = new List<DateOnly>(Returns + 1);
        for (var back = 1; back <= LookbackDays && sessions.Count < Returns + 1; back++)
        {
            var date = tradeDate.AddDays(-back);
            if (calendar.IsTradingDay(date))
            {
                sessions.Add(date);
            }
        }

        if (sessions.Count < Returns + 1)
        {
            return FeatureCell.Unknown(FeatureReasons.ShortHistory);
        }

        var logs = new double[Returns];
        for (var i = 0; i < Returns; i++)
        {
            var later = sessions[i];
            var earlier = sessions[i + 1];
            if (!closes.TryGetValue(later, out var end) || !closes.TryGetValue(earlier, out var start) || end <= 0 || start <= 0)
            {
                return FeatureCell.Unknown(FeatureReasons.ShortHistory);
            }

            logs[i] = Math.Log(end / (start * EpsAdjustment.Factor(actions, earlier, later)));
        }

        var mean = logs.Average();
        var variance = logs.Sum(r => (r - mean) * (r - mean)) / (Returns - 1);
        var annual = Math.Sqrt(variance) * Math.Sqrt(252.0);
        return double.IsFinite(annual) ? FeatureCell.Known(annual) : FeatureCell.Unknown(FeatureReasons.Degenerate);
    }
}
