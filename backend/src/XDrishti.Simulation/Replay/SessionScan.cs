using System.Collections.ObjectModel;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research;

namespace XDrishti.Simulation.Replay;

/// <summary>A date whose session runs: the date and the bars inside the window of its session, as positions in the raw list (<paramref name="End"/> is exclusive).</summary>
internal readonly record struct RanSession(DateOnly Date, int Start, int End)
{
    public int Count => End - Start;
}

/// <summary>
/// The data notes, part one (spec P3-04, Scope 4): every date with bars is classified with <c>TradingCalendar.SessionOf</c> and the bars inside its window. No session on the date is a closed day; a session that is not the regular
/// 09:15 to 15:30 window is a special session; a regular session whose window holds fewer than <see cref="ReplayOptions.MinSessionCompleteness"/> of the 375 minutes, compared in <c>decimal</c> so that exactly the threshold runs, is a
/// short session; otherwise it runs. A trading day of the calendar between the first and the last bar with no bar at all is a missing session. Bars outside the window of a session that runs are dropped and counted. The window test is the one of
/// <c>CompletenessChecker</c> (minute of the day in <c>[FromMinute, ToMinute)</c>); the checker itself is not reused because it holds the first day of an instrument against no one and reports a percent missing, not a fraction present.
/// </summary>
internal sealed class SessionScan
{
    private SessionScan(
        List<RanSession> ran,
        SessionCounts counts,
        ReadOnlyCollection<SessionNote> notes,
        bool notesTruncated,
        long barsFed,
        long barsExcluded,
        long barsOutsideSession)
    {
        Ran = ran;
        Counts = counts;
        Notes = notes;
        NotesTruncated = notesTruncated;
        BarsFed = barsFed;
        BarsExcluded = barsExcluded;
        BarsOutsideSession = barsOutsideSession;
    }

    /// <summary>The sessions that run, in date order.</summary>
    public IReadOnlyList<RanSession> Ran { get; }

    public SessionCounts Counts { get; }

    /// <summary>One note per excluded session, in date order, at most <see cref="InstrumentRun.MaxNotes"/>.</summary>
    public IReadOnlyList<SessionNote> Notes { get; }

    public bool NotesTruncated { get; }

    /// <summary>The bars inside the windows of the sessions that run.</summary>
    public long BarsFed { get; }

    /// <summary>The bars of the excluded sessions plus <see cref="BarsOutsideSession"/>.</summary>
    public long BarsExcluded { get; }

    /// <summary>The bars outside the window of a session that runs.</summary>
    public long BarsOutsideSession { get; }

    /// <summary>Classifies the bars (strictly ascending, not empty).</summary>
    public static SessionScan Classify(IReadOnlyList<Bar> bars, TradingCalendar calendar, ReplayOptions options)
    {
        var ran = new List<RanSession>();
        var notes = new List<SessionNote>();
        long closed = 0, special = 0, shortSessions = 0, missing = 0, fed = 0, excluded = 0, outside = 0;
        var required = (decimal)options.MinSessionCompleteness * SessionClock.RegularBars;
        DateOnly? previous = null;
        var i = 0;
        while (i < bars.Count)
        {
            var day = DayOf(bars[i].EpochSeconds);
            var date = SessionClock.DateOf(bars[i].EpochSeconds);
            if (previous is { } before)
            {
                for (var gap = before.AddDays(1); gap < date; gap = gap.AddDays(1))
                {
                    if (calendar.IsTradingDay(gap))
                    {
                        missing++;
                        Note(notes, new SessionNote(gap, SessionExclusion.MissingSession, 0, 0));
                    }
                }
            }

            previous = date;
            var session = calendar.SessionOf(date);
            var j = i;
            var inWindow = 0;
            var windowFirst = -1;
            while (j < bars.Count && DayOf(bars[j].EpochSeconds) == day)
            {
                if (session is { } s)
                {
                    var minute = SessionClock.MinuteOfDay(bars[j].EpochSeconds);
                    if (minute >= s.FromMinute && minute < s.ToMinute)
                    {
                        if (windowFirst < 0)
                        {
                            windowFirst = j;
                        }

                        inWindow++;
                    }
                }

                j++;
            }

            var size = j - i;
            if (session is not { } found)
            {
                closed++;
                excluded += size;
                Note(notes, new SessionNote(date, SessionExclusion.ClosedDay, 0, 0));
            }
            else if (!found.IsRegular)
            {
                special++;
                excluded += size;
                Note(notes, new SessionNote(date, SessionExclusion.SpecialSession, inWindow, found.Minutes));
            }
            else if (inWindow < required)
            {
                shortSessions++;
                excluded += size;
                Note(notes, new SessionNote(date, SessionExclusion.ShortSession, inWindow, found.Minutes));
            }
            else
            {
                ran.Add(new RanSession(date, windowFirst, windowFirst + inWindow));
                fed += inWindow;
                outside += size - inWindow;
                excluded += size - inWindow;
            }

            i = j;
        }

        var truncated = notes.Count > InstrumentRun.MaxNotes;
        var kept = notes.Take(InstrumentRun.MaxNotes).ToList().AsReadOnly();
        return new SessionScan(ran, new SessionCounts(ran.Count, closed, special, shortSessions, missing), kept, truncated, fed, excluded, outside);
    }

    // The notes are kept one past the limit so that the truncation can be told; the counts are exact whatever is kept.
    private static void Note(List<SessionNote> notes, SessionNote note)
    {
        if (notes.Count <= InstrumentRun.MaxNotes)
        {
            notes.Add(note);
        }
    }

    // The India day number of an instant: whole days since 1970-01-01 of the instant shifted by 05:30 (floored, so that an instant before 1970 is still in the right day).
    private static long DayOf(long epochSeconds) => Math.DivRem(epochSeconds + (330 * 60L), 86_400L, out var rest) - (rest < 0 ? 1 : 0);
}
