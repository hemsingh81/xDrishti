using static XDrishti.Simulation.Tests.Oracle.Fx;

namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>Rows K1 to K7 of spec P3-03d: the due session and the due minute.</summary>
public sealed class CalendarTests
{
    public static TheoryData<string, string, int, string, string, int> KRows() => new()
    {
        { "K1", Mon, 1, "none", Mon, 929 },
        { "K2", Mon, 3, "none", Wed, 929 },
        { "K3", Mon, 3, "holiday-tue", Thu, 929 },
        { "K4", Mon, 3, "none", Wed, 929 },
        { "K5", Thu, 3, "none", Mon2, 929 },
        { "K6", Fri, 2, "special-sat", Sat, 749 },
        { "K7", Thu, 5, "none", "2026-03-11", 929 },
    };

    private static OracleCalendar CalendarOf(string kind) => kind switch
    {
        "holiday-tue" => Cal([Tue]),
        "special-sat" => Cal([], (Sat, "09:15", "12:30")),
        _ => OracleCalendar.Default,
    };

    [Theory]
    [MemberData(nameof(KRows))]
    public void The_due_session_and_the_due_minute(string id, string entry, int n, string calendar, string due, int dueMinute)
    {
        OracleCalendar cal = CalendarOf(calendar);
        DateOnly t = cal.AddTradingDays(D(entry), n - 1);
        t.ShouldBe(D(due), id);
        cal.DueMinute(t).ShouldBe(dueMinute, id);
    }

    [Fact]
    public void K6_through_the_walker_the_exit_is_the_close_of_the_12_29_minute_of_the_special_session()
    {
        OracleCalendar cal = Cal([], (Sat, "09:15", "12:30"));
        Tape t = new Tape()
            .Day(Fri, "09:15", "15:29", 100.00m, [M("10:00", 100.00m, 100.30m, 99.80m, 100.20m)])
            .Day(Sat, "09:15", "12:29", listed: [M("12:29", 100.30m, 100.50m, 100.20m, 100.40m)])
            .Day(Mon2, "09:15", "15:29");
        OracleCandidate c = Cand(t, OracleSide.Buy, Fri, "10:00", 100.00m, 99.00m);
        OracleResult r = ReferenceWalker.Walk(t.ToArray(), c, Swing(2, cal), OracleFaults.None);
        r.Trade!.ExitReason.ShouldBe(OracleExitReason.MaxHold);
        r.Trade.ExitEpochSeconds.ShouldBe(E(Sat, "12:29"));
        r.Trade.ExitPrice.ShouldBe(100.40);
        r.Trade.SessionsHeld.ShouldBe(2);
        r.Rows.ShouldContain(OracleRows.CalSpecial);
    }

    [Fact]
    public void A_special_session_counts_as_a_session_even_when_it_has_no_minutes_in_the_list()
    {
        // K6 with the replay's exclusion of the special session: the position leaves at Friday's close, one session short.
        OracleCalendar cal = Cal([], (Sat, "09:15", "12:30"));
        Tape t = new Tape()
            .Day(Fri, "09:15", "15:29", 100.00m, [M("10:00", 100.00m, 100.30m, 99.80m, 100.20m)])
            .Day(Mon2, "09:15", "15:29");
        OracleCandidate c = Cand(t, OracleSide.Buy, Fri, "10:00", 100.00m, 99.00m);
        OracleResult r = ReferenceWalker.Walk(t.ToArray(), c, Swing(2, cal), OracleFaults.None);
        r.Trade!.ExitReason.ShouldBe(OracleExitReason.MaxHold);
        r.Counters.MaxHoldAtLastClose.ShouldBe(1);
        r.Trade.ExitEpochSeconds.ShouldBe(E(Fri, "15:29"));
        r.Trade.SessionsHeld.ShouldBe(1);
    }

    [Fact]
    public void Trading_days_between_counts_the_days_after_the_first_up_to_the_second()
    {
        OracleCalendar cal = Cal([Tue]);
        cal.TradingDaysBetween(D(Mon), D(Thu)).ShouldBe(2);
        OracleCalendar.Default.TradingDaysBetween(D(Mon), D(Thu)).ShouldBe(3);
        OracleCalendar.Default.TradingDaysBetween(D(Fri), D(Mon2)).ShouldBe(1);
    }
}
