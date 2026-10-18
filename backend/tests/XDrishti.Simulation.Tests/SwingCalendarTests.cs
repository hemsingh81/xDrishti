using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Simulation.Walking;
using static XDrishti.Simulation.Tests.Support.TestCalendars;

namespace XDrishti.Simulation.Tests;

/// <summary>
/// Criteria 5 and 8 of P3-03d: the due session of a hold by the trading calendar (K1 to K7, with the real <see cref="TradingCalendar"/> and with none), the validation of the hold and of the rules, and the search for the next trading day.
/// </summary>
public sealed class SwingCalendarTests
{
    /// <summary>The seven rows of D3: entry date, N, the calendar, the due date and the due minute, and the sessions on which the list has bars.</summary>
    public static TheoryData<string, DateOnly, int, string, DateOnly, string> Rows => new()
    {
        { "K1", Mon, 1, "any", Mon, "15:29" },
        { "K2", Mon, 3, "default", Wed, "15:29" },
        { "K3", Mon, 3, "holiday", Thu, "15:29" },
        { "K4", Mon, 3, "none", Wed, "15:29" },
        { "K5", Thu, 3, "default", NextMon, "15:29" },
        { "K6", Fri, 2, "special", Sat, "12:29" },
        { "K7", Thu, 5, "default", new DateOnly(2026, 3, 11), "15:29" },
    };

    private static TradingCalendar? CalendarOf(string kind) => kind switch
    {
        "holiday" => HolidayOnTuesday(),
        "special" => SpecialSaturday(),
        "default" => DefaultWeek(),
        "any" => DefaultWeek(),
        _ => null,
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void The_due_session_and_the_due_minute_equal_the_table(string name, DateOnly entry, int sessions, string calendar, DateOnly due, string dueMinute)
    {
        var hold = SwingHold.Create(sessions, CalendarOf(calendar)).Value;

        var found = hold.DueSession(entry);

        found.IsSuccess.ShouldBeTrue(name);
        found.Value.ShouldBe(due, name);
        hold.DueMinute(found.Value).ShouldBe(Day.Minute(dueMinute), name);
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void A_walk_over_flat_sessions_leaves_at_the_close_of_the_due_minute_on_the_due_date(string name, DateOnly entry, int sessions, string calendar, DateOnly due, string dueMinute)
    {
        var cal = CalendarOf(calendar);
        var hand = SwingHand.A(name, sessions, cal, entry);
        for (var date = entry.AddDays(1); date <= due; date = date.AddDays(1))
        {
            // The list holds the sessions of the truth: K4 has no calendar and the list has no Tuesday; the others list every session of the calendar up to the due date.
            var inList = calendar == "none" ? date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && date != Tue : (cal ?? DefaultWeek()).IsTradingDay(date);
            if (!inList)
            {
                continue;
            }

            if (calendar == "special" && date == Sat)
            {
                hand.ThenWith(date, "12:29", null);
            }
            else
            {
                hand.Flat(date);
            }
        }

        var trade = hand.Walk().Trade.ShouldNotBeNull(name);

        trade.ExitReason.ShouldBe(ExitReason.MaxHold, name);
        trade.ExitEpochSeconds.ShouldBe(Day.Epoch(due, Day.Minute(dueMinute)), name);
        trade.ExitPrice.ShouldBe(100.20, name);
    }

    [Fact]
    public void K3_and_K4_differ_the_holiday_is_a_session_without_the_calendar_and_one_session_earlier()
    {
        var withCalendar = SwingHold.Create(3, HolidayOnTuesday()).Value.DueSession(Mon).Value;
        var without = SwingHold.Create(3, null).Value.DueSession(Mon).Value;

        withCalendar.ShouldBe(Thu);
        without.ShouldBe(Wed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public void A_hold_outside_one_to_five_sessions_is_refused(int sessions)
    {
        var result = SwingHold.Create(sessions, null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("simulation.rules_invalid");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void A_hold_of_one_or_five_sessions_is_accepted_and_keeps_what_it_was_given(int sessions)
    {
        var calendar = HolidayOnTuesday();

        var hold = SwingHold.Create(sessions, calendar).Value;

        hold.MaxHoldSessions.ShouldBe(sessions);
        hold.Calendar.ShouldBeSameAs(calendar);
    }

    [Fact]
    public void A_hold_with_a_square_off_and_a_walk_without_a_hold_and_with_no_square_off_are_refused()
    {
        var hand = SwingHand.A("rules", 3);
        var bars = hand.Bars;
        var hold = SwingHold.Create(3, null).Value;
        var tick = TickSpec.FromRupees(0.05m).Value;

        var swingWithSquareOff = TradeWalker.Walk(hand.Candidate(), bars, new WalkRules(2.0, 915, FillRules.Default, tick, hold));
        var intradayWithoutSquareOff = TradeWalker.Walk(hand.Candidate(), bars, new WalkRules(2.0, WalkRules.NoSquareOff, FillRules.Default, tick));
        var intradayPastTheDay = TradeWalker.Walk(hand.Candidate(), bars, new WalkRules(2.0, 1440, FillRules.Default, tick));

        swingWithSquareOff.Error.Code.ShouldBe("simulation.rules_invalid");
        intradayWithoutSquareOff.Error.Code.ShouldBe("simulation.rules_invalid");
        intradayPastTheDay.Error.Code.ShouldBe("simulation.rules_invalid");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1439)]
    public void An_intraday_walk_keeps_accepting_a_square_off_from_1_to_1439(int minute)
    {
        var hand = SwingHand.A("intraday-rules", 3);

        var result = TradeWalker.Walk(hand.Candidate(), hand.Bars, new WalkRules(2.0, minute, FillRules.Default, TickSpec.FromRupees(0.05m).Value));

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_calendar_in_which_no_trading_day_is_found_within_thirty_days_is_refused_and_does_not_loop()
    {
        var calendar = NoTradingDays(Mon);
        var hold = SwingHold.Create(3, calendar).Value;
        var hand = SwingHand.A("no-days", 3, calendar);

        var found = hold.DueSession(Mon);
        var walked = TradeWalker.Walk(hand.Candidate(), hand.Bars, hand.Rules(FillRules.Default));

        found.IsFailure.ShouldBeTrue();
        found.Error.Code.ShouldBe("simulation.rules_invalid");
        walked.IsFailure.ShouldBeTrue();
        walked.Error.Code.ShouldBe("simulation.rules_invalid");
    }

    [Fact]
    public void A_one_session_hold_never_searches_the_calendar_so_it_is_accepted_even_on_a_calendar_of_holidays()
    {
        var hold = SwingHold.Create(1, NoTradingDays(Mon)).Value;

        hold.DueSession(Mon).Value.ShouldBe(Mon);
    }

    [Fact]
    public void The_sessions_held_count_the_entry_session_and_every_trading_day_up_to_the_exit_date()
    {
        var hold = SwingHold.Create(5, HolidayOnTuesday()).Value;

        hold.SessionsBetween(Mon, Mon).ShouldBe(1);
        hold.SessionsBetween(Mon, Wed).ShouldBe(2, "Tuesday is a holiday");
        hold.SessionsBetween(Thu, NextMon).ShouldBe(3, "Friday and Monday; the weekend is not a session");
        SwingHold.Create(5, SpecialSaturday()).Value.SessionsBetween(Fri, Sat).ShouldBe(2, "a special Saturday session counts");
    }

    [Fact]
    public void The_end_of_the_due_session_is_the_end_of_its_own_window_1530_regular_and_1230_special()
    {
        SwingHold.Create(2, null).Value.SessionEnd(Mon).ShouldBe(Day.Epoch(Mon, Day.Minute("15:30")));
        SwingHold.Create(2, SpecialSaturday()).Value.SessionEnd(Sat).ShouldBe(Day.Epoch(Sat, Day.Minute("12:30")));
    }

    [Fact]
    public void The_hold_is_a_record_with_value_equality_and_no_public_constructor()
    {
        var calendar = DefaultWeek();

        SwingHold.Create(3, calendar).Value.ShouldBe(SwingHold.Create(3, calendar).Value);
        SwingHold.Create(3, calendar).Value.ShouldNotBe(SwingHold.Create(4, calendar).Value);
        typeof(SwingHold).GetConstructors().ShouldBeEmpty();
        typeof(SwingHold).IsSealed.ShouldBeTrue();
    }
}
