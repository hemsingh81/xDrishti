using XDrishti.Domain.Market.Bars;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>
/// Fixture R of P3-03d, the 1h swing fixture of criterion 13 (used by J, Q and the replay tests). Monday (dense, starts 99.00): <c>09:20 99.00/100.00/99.00/99.00</c>, <c>09:25 99.00/99.00/98.00/99.00</c>, <c>10:30 99.50/101.50/99.50/101.50</c>,
/// flat 101.50 after. The 1h bar of 10:15 to 11:15 closes at 101.50 above the high of the first hour (100.00): decision Monday 11:15, trigger close 101.50, stop 98.00; entry 101.50 at the open of 11:15, risk 3.50, target 108.50.
/// Tuesday and Wednesday are flat at 101.50 except Wednesday <c>15:29 101.60/101.80/101.50/101.70</c>; Thursday and Friday are flat at 101.70. Nothing stops the position, so it leaves <c>MaxHold</c> at Wednesday's close.
/// </summary>
internal static class FixtureR
{
    public static readonly DateOnly Mon = TestCalendars.Mon;

    public static Day Monday() => Day.Dense(Mon).Start(99.00).Set("09:20 99.00/100.00/99.00/99.00", "09:25 99.00/99.00/98.00/99.00", "10:30 99.50/101.50/99.50/101.50");

    /// <summary>Tuesday after Monday: flat 101.50 unless rows are given.</summary>
    public static Day Tuesday(params string[] rows) => Day.After(Monday(), TestCalendars.Tue).Set(rows);

    /// <summary>The bars of the fixture from Monday through <paramref name="last"/> (Monday by default through Friday), Tuesday as given.</summary>
    public static Bar[] Bars(DateOnly last, params string[] tuesday)
    {
        var monday = Monday();
        var days = new List<Day> { monday };
        var tue = Day.After(monday, TestCalendars.Tue).Set(tuesday);
        if (last >= TestCalendars.Tue)
        {
            days.Add(tue);
        }

        var wed = Day.After(tue, TestCalendars.Wed).Set("15:29 101.60/101.80/101.50/101.70");
        if (last >= TestCalendars.Wed)
        {
            days.Add(wed);
        }

        var thu = Day.After(wed, TestCalendars.Thu);
        if (last >= TestCalendars.Thu)
        {
            days.Add(thu);
        }

        if (last >= TestCalendars.Fri)
        {
            days.Add(Day.After(thu, TestCalendars.Fri));
        }

        return Day.Join([.. days]);
    }

    /// <summary>The same fixture when Tuesday is flat at another level and Wednesday and later repeat it (the flagged and the unflagged crash of Q1 and Q2).</summary>
    public static Bar[] CrashBars(DateOnly last, string tuesdayFirstMinute)
    {
        var monday = Monday();
        var tuesday = Day.After(monday, TestCalendars.Tue).Set(tuesdayFirstMinute);
        var days = new List<Day> { monday, tuesday };
        var previous = tuesday;
        foreach (var date in new[] { TestCalendars.Wed, TestCalendars.Thu, TestCalendars.Fri })
        {
            if (date > last)
            {
                break;
            }

            var next = Day.After(previous, date);
            days.Add(next);
            previous = next;
        }

        return Day.Join([.. days]);
    }
}
