using XDrishti.Domain.Market.Bars;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>
/// The 12 invented sessions of one instrument of criterion 10 (P3-03e), the fixture that P3-06 extends. The test strategy is <c>SWING_DAILY_TEST</c> with the trigger <c>close &gt; open</c> (a candidate on an up day only), the stop on the low of the
/// daily bar and a hold of five sessions. Sessions: S1 Mon 2026-03-02 to S5 Fri 03-06, S6 Mon 03-09 to S10 Fri 03-13, S11 Mon 03-16, S12 Tue 03-17; every minute holds a trade; a day that is not listed otherwise is flat.
/// <list type="bullet">
/// <item>S1 up day (open 99.00, close 100.00, low 98.00): candidate C1, limit 100.10. S2 opens 100.05: filled at the open. S2 to S6 flat but S4 (an up day, close 100.50): C4 is decided inside the position, <c>PositionOpen</c>. The position leaves at the
/// close of S6, its fifth session: <c>MaxHold</c> at 100.50.</item>
/// <item>S7 up day (open 100.50, close 101.00, low 100.00): candidate C7, limit 101.10; S8 is flat at 103.00, above the limit all day: <c>LimitNotReached</c>, a counted miss.</item>
/// <item>S9 up day (open 103.00, close 104.00, low 102.50): candidate C9, limit 104.10; S10 opens 104.05 and is flat: filled at the open, stop 102.50, risk 1.55. S11 opens at 90.00 (13.5 % under the close, not flagged): <c>Stop</c> at the open, a gap through the stop.</item>
/// <item>S12 up day (open 90.00, close 91.00): candidate C12, decided at 15:30 of the last session: <c>OrderPendingAtEnd</c>.</item>
/// </list>
/// </summary>
internal static class DailyFixture
{
    public static readonly IReadOnlyList<DateOnly> Dates =
    [
        new(2026, 3, 2), new(2026, 3, 3), new(2026, 3, 4), new(2026, 3, 5), new(2026, 3, 6),
        new(2026, 3, 9), new(2026, 3, 10), new(2026, 3, 11), new(2026, 3, 12), new(2026, 3, 13),
        new(2026, 3, 16), new(2026, 3, 17),
    ];

    public const string Trigger = "close > open";

    /// <summary>The bars of the sessions from S1 to the session numbered <paramref name="sessions"/> (1 to 12).</summary>
    public static Bar[] Twelve(int sessions = 12)
    {
        var days = new List<Day>
        {
            Day.Dense(Dates[0]).Start(99.00).Set("10:00 99.00/100.00/98.00/100.00"),
            Day.Dense(Dates[1]).Start(100.05),
            Day.Dense(Dates[2]).Start(100.05),
            Day.Dense(Dates[3]).Start(100.05).Set("10:00 100.05/101.00/100.05/100.50"),
            Day.Dense(Dates[4]).Start(100.50),
            Day.Dense(Dates[5]).Start(100.50),
            Day.Dense(Dates[6]).Start(100.50).Set("10:00 100.50/101.50/100.00/101.00"),
            Day.Dense(Dates[7]).Start(103.00),
            Day.Dense(Dates[8]).Start(103.00).Set("10:00 103.00/104.50/102.50/104.00"),
            Day.Dense(Dates[9]).Start(104.05),
            Day.Dense(Dates[10]).Start(90.00),
            Day.Dense(Dates[11]).Start(90.00).Set("10:00 90.00/91.50/90.00/91.00"),
        };
        return Day.Join([.. days.Take(sessions)]);
    }
}
