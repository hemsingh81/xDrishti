using System.Globalization;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Market.Bars.Dhan;

/// <summary>
/// Looks for a wrong reading of a source's timestamps in the bars of one window (ADR 0016 §7), before any bar is stored. The documentation of Dhan's chart
/// endpoint does not say which time base or time zone its <c>timestamp</c> uses, so the fetch assumes Unix time and this check proves it from the data:
/// <list type="bullet">
/// <item>most bars lie more than a day outside the period that was asked for → the base is not Unix time (for example another epoch);</item>
/// <item>most bars lie outside every trading session (India time) but <i>all</i> of them lie inside one after adding or subtracting 5 h 30 → the classic
/// offset problem (India time read as UTC, or the reverse).</item>
/// </list>
/// It only names the suspicion; nothing is shifted or guessed. A few stray bars outside a session are not an offset problem: <see cref="IntradayBarCheck"/>
/// rejects and counts them.
/// </summary>
public static class IntradayAlignmentCheck
{
    private const int HalfHourOffsetMinutes = 330;

    private static readonly TimeSpan PeriodTolerance = TimeSpan.FromDays(1);

    /// <summary>Null when the bars look right (or there are none); otherwise a safe message (dates and minutes only) for the owner.</summary>
    public static string? Problem(IReadOnlyList<Bar> bars, FetchWindow window)
    {
        ArgumentNullException.ThrowIfNull(bars);
        if (bars.Count == 0)
        {
            return null;
        }

        var earliest = window.From - PeriodTolerance;
        var latest = window.To + PeriodTolerance;
        var outsidePeriod = bars.Count(b => b.Start < earliest || b.Start > latest);
        if (outsidePeriod * 2 > bars.Count)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"the timestamps do not fit the period asked for ({IndiaTime.ToIst(window.From):yyyy-MM-dd} to {IndiaTime.ToIst(window.To):yyyy-MM-dd}): the first bar is dated {bars[0].Start:yyyy-MM-dd}, so the time base of the timestamps is not Unix time (nothing was stored)");
        }

        var problem = Offset(bars);
        if (problem is not null)
        {
            return problem;
        }

        // A bar starting at 15:30 cannot exist, and without any 09:15 bar: all bars lie between 09:16 and 15:30, so they are probably stamped with the minute they END.
        var minutes = bars.Select(b => SessionClock.MinuteOfDay(b.EpochSeconds)).ToList();
        return minutes.Contains(SessionClock.CloseMinute) && minutes.All(m => m is > SessionClock.OpenMinute and <= SessionClock.CloseMinute)
            ? "the bars include one stamped 15:30 (India time) but none stamped 09:15: the timestamps are probably the END of each minute, not its start (nothing was shifted or stored)"
            : null;
    }

    private static string? Offset(IReadOnlyList<Bar> bars)
    {
        // Bars that all lie inside an accepted session (a regular one or a special evening one, like Muhurat) are fine as they are. Otherwise count against the regular
        // session: a whole day shifted by 5 h 30 min still lands partly inside the evening window, which must not hide the shift.
        if (bars.All(b => SessionClock.InAnySession(SessionClock.MinuteOfDay(b.EpochSeconds))))
        {
            return null;
        }

        var outsideSession = bars.Count(b => !SessionClock.InRegularSession(SessionClock.MinuteOfDay(b.EpochSeconds)));
        if (outsideSession * 2 <= bars.Count)
        {
            return null;
        }

        foreach (var (minutes, wording) in new[] { (HalfHourOffsetMinutes, "adding"), (-HalfHourOffsetMinutes, "subtracting") })
        {
            if (bars.All(b => SessionClock.InAnySession(Shifted(b, minutes))))
            {
                return string.Create(
                    CultureInfo.InvariantCulture,
                    $"{outsideSession} of {bars.Count} bars lie outside the regular trading session (India time), but all of them fit a session after {wording} 5 h 30 min: the timestamps are probably off by the India/UTC offset (nothing was shifted or stored)");
            }
        }

        return null;
    }

    private static int Shifted(Bar bar, int minutes) => SessionClock.MinuteOfDay(bar.EpochSeconds + (minutes * 60L));
}
