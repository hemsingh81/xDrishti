using System.Globalization;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Market.Bars.Csv;

/// <summary>
/// The session and alignment check of one CSV file (ADR 0016 §4, §7): the streaming counterpart of <see cref="Dhan.IntradayAlignmentCheck"/>, which looks at the
/// bars of one fetch window at once. Every accepted bar of the file is counted (a few integer operations each, so a 40-million-row load is not slowed down) and the
/// verdict is given at the end, before anything is merged, so a file whose clock is wrong is refused as a whole instead of putting 200 000 bars at the wrong minutes:
/// <list type="bullet">
/// <item>most bars lie outside the regular session (India time) but nearly all of them (at least <see cref="ShiftShare"/>) fit a session after adding or subtracting
/// 5 h 30 min → the classic offset problem (India time read as UTC, or the reverse);</item>
/// <item>most bars lie outside every session (regular or special evening) → the times are not India market minutes at all;</item>
/// <item>a bar is stamped 15:30, none 09:15, and none lies outside 09:16–15:30 → the stamps are the END of each minute.</item>
/// </list>
/// A few stray bars never trigger it: the real files hold the 60-bar Muhurat evening session and Saturday special sessions, both legitimate. Nothing is shifted
/// or guessed; the verdict names the suspicion in dates and counts only.
/// </summary>
internal sealed class CsvSessionProbe
{
    /// <summary>The share of the bars (in per cent) that must fit a session after a 5 h 30 min shift for the shift to be named.</summary>
    private const int ShiftShare = 99;

    private const int OffsetMinutes = 330;

    private const int MinutesPerDay = 24 * 60;

    private long _total;
    private long _outsideRegular;
    private long _outsideAny;
    private long _fitAfterAdding;
    private long _fitAfterSubtracting;
    private long _atOpen;
    private long _atClose;
    private long _outsideEndWindow;

    public void Add(long epochSeconds)
    {
        _total++;
        var minute = SessionClock.MinuteOfDay(epochSeconds);
        if (SessionClock.InRegularSession(minute))
        {
            if (minute == SessionClock.OpenMinute)
            {
                _atOpen++;
            }
        }
        else
        {
            _outsideRegular++;
            if (!SessionClock.InAnySession(minute))
            {
                _outsideAny++;
            }
        }

        if (minute is > SessionClock.OpenMinute and <= SessionClock.CloseMinute)
        {
            if (minute == SessionClock.CloseMinute)
            {
                _atClose++;
            }
        }
        else
        {
            _outsideEndWindow++;
        }

        if (SessionClock.InAnySession((minute + OffsetMinutes) % MinutesPerDay))
        {
            _fitAfterAdding++;
        }

        if (SessionClock.InAnySession((minute - OffsetMinutes + MinutesPerDay) % MinutesPerDay))
        {
            _fitAfterSubtracting++;
        }
    }

    /// <summary>Null when the bars look right (or there are none); otherwise a safe message for the owner.</summary>
    public string? Problem()
    {
        if (_total == 0)
        {
            return null;
        }

        // Bars that all lie inside a session (a regular one or a special evening one, like Muhurat) are fine as they are.
        if (_outsideAny > 0 && _outsideRegular * 2 > _total)
        {
            foreach (var (fit, wording) in new[] { (_fitAfterAdding, "adding"), (_fitAfterSubtracting, "subtracting") })
            {
                if (fit * 100 >= _total * ShiftShare)
                {
                    return string.Create(
                        CultureInfo.InvariantCulture,
                        $"{_outsideRegular:N0} of {_total:N0} bars lie outside the regular trading session (India time), but {fit:N0} of them fit a session after {wording} 5 h 30 min: the timestamps are probably off by the India/UTC offset (nothing was shifted or stored)");
                }
            }
        }

        if (_outsideAny * 2 > _total)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{_outsideAny:N0} of {_total:N0} bars lie outside every trading session (India time) and no 5 h 30 min shift explains it: the times are probably not India market minutes (nothing was stored)");
        }

        // A bar starting at 15:30 cannot exist; with no 09:15 bar either, all bars lie between 09:16 and 15:30: they are probably stamped with the minute they END.
        return _atClose > 0 && _atOpen == 0 && _outsideEndWindow == 0
            ? "the file has bars stamped 15:30 (India time) but none stamped 09:15: the timestamps are probably the END of each minute, not its start (nothing was shifted or stored)"
            : null;
    }
}
