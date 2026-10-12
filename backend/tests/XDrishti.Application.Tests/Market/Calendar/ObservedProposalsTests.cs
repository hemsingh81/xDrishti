using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Market.Calendar;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Tests.Market.Calendar;

/// <summary>
/// The observed rule on the data shapes that matter (ADR 0017 §1): 2024-11-01 is the Muhurat evening (60 bars from 18:15 for every instrument), two Saturdays have
/// 60-bar sessions, ordinary weekdays have 375 bars, and a weekday with almost no bars is suspected a holiday — but only suspected.
/// </summary>
public sealed class ObservedProposalsTests
{
    private const int Instruments = 214;

    private static readonly DateOnly First = new(2024, 10, 14);

    private static readonly DateOnly Last = new(2024, 11, 15);

    private static readonly IReadOnlyDictionary<DateOnly, ObservedWindow> NoWindows = new Dictionary<DateOnly, ObservedWindow>();

    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    private static PresenceBuilder Universe(int count = Instruments) => new PresenceBuilder().Weekdays(count, First, Last);

    private static IReadOnlyList<ObservedProposal> Build(DayPresence presence, IReadOnlyDictionary<DateOnly, ObservedWindow>? windows = null) =>
        ObservedProposals.Build(presence, windows ?? NoWindows);

    [Fact]
    public void Ordinary_weekdays_with_normal_coverage_propose_nothing()
    {
        var presence = Universe().Build();

        Build(presence).ShouldBeEmpty();
        ObservedProposals.WindowCandidates(presence).ShouldBeEmpty();
    }

    [Fact]
    public void Nothing_stored_proposes_nothing() => Build(new DayPresence(false, [], [])).ShouldBeEmpty();

    [Fact]
    public void A_weekday_without_a_single_bar_between_two_trading_days_is_a_suspected_holiday()
    {
        var presence = new PresenceBuilder().Weekdays(Instruments, First, Last, (_, d) => d != D(2024, 10, 31)).Build();

        var proposal = Build(presence).ShouldHaveSingleItem();

        (proposal.Date, proposal.Kind, proposal.FromMinute, proposal.ToMinute).ShouldBe((D(2024, 10, 31), CalendarDayKind.Holiday, null, null));
        proposal.Note.ShouldBe("Seen in the stored bars: 0 of 214 instruments with bars before and after this date have bars on it.");
    }

    [Fact]
    public void Two_holidays_in_a_row_are_both_suspected()
    {
        var presence = new PresenceBuilder().Weekdays(Instruments, First, Last, (_, d) => d != D(2024, 10, 30) && d != D(2024, 10, 31)).Build();

        Build(presence).Select(p => p.Date).ShouldBe([D(2024, 10, 30), D(2024, 10, 31)]);
    }

    [Theory]
    [InlineData(10, true)]   // 4.7 % of 214
    [InlineData(11, false)]  // 5.1 %
    [InlineData(0, true)]
    [InlineData(100, false)]
    public void A_weekday_with_only_a_few_instruments_is_a_holiday_below_five_percent_of_those_around_it(int present, bool proposed)
    {
        var presence = new PresenceBuilder().Weekdays(Instruments, First, Last, (i, d) => d != D(2024, 10, 31) || i < present).Build();

        Build(presence).Select(p => p.Date).ShouldBe(proposed ? [D(2024, 10, 31)] : []);
    }

    [Theory]
    [InlineData(4, true)]
    [InlineData(5, false)] // exactly 5 %: "fewer than" it
    public void Five_percent_itself_is_not_a_holiday(int present, bool proposed)
    {
        var presence = new PresenceBuilder().Weekdays(100, First, Last, (i, d) => d != D(2024, 10, 31) || i < present).Build();

        Build(presence).Select(p => p.Date).ShouldBe(proposed ? [D(2024, 10, 31)] : []);
    }

    [Fact]
    public void A_weekday_on_which_some_instruments_lack_bars_is_not_a_holiday()
    {
        // 20 % of the instruments (a vendor gap in some files) miss three days: the market was open.
        var presence = new PresenceBuilder().Weekdays(Instruments, First, Last, (i, d) => i % 5 != 0 || d.Day is < 28 or > 30).Build();

        Build(presence).ShouldBeEmpty();
    }

    [Fact]
    public void Instruments_that_start_later_or_end_earlier_do_not_make_a_day_a_holiday()
    {
        var presence = new PresenceBuilder()
            .Weekdays(100, First, new DateOnly(2024, 10, 25))
            .Weekdays(100, new DateOnly(2024, 10, 28), Last)
            .Build();

        // Nobody has bars on both sides of 10-26..10-27 (a weekend anyway) and 10-28 is the second group's first day: nothing is suspected.
        Build(presence).ShouldBeEmpty();
    }

    [Fact]
    public void Dates_before_the_first_and_after_the_last_bar_are_never_holidays()
    {
        var presence = new PresenceBuilder().Weekdays(50, new DateOnly(2024, 10, 21), new DateOnly(2024, 10, 25)).Build();

        Build(presence).ShouldBeEmpty();
    }

    [Fact]
    public void A_single_instrument_with_a_gap_is_suspected_too_but_it_is_only_a_proposal()
    {
        var presence = new PresenceBuilder().Weekdays(1, First, Last, (_, d) => d != D(2024, 10, 22)).Build();

        Build(presence).ShouldHaveSingleItem().Date.ShouldBe(D(2024, 10, 22));
    }

    [Fact]
    public void A_confirmed_holiday_with_a_regular_session_in_the_bars_is_proposed_as_a_special_session()
    {
        var moved = D(2024, 10, 22);
        var presence = Universe().Build();
        var holidays = new HashSet<DateOnly> { moved };
        var windows = new Dictionary<DateOnly, ObservedWindow> { [moved] = new(Instruments, SessionClock.OpenMinute, SessionClock.CloseMinute) };

        ObservedProposals.WindowCandidates(presence, holidays).ShouldBe([moved]);
        var proposal = ObservedProposals.Build(presence, windows, holidays).ShouldHaveSingleItem();

        (proposal.Date, proposal.Kind, proposal.FromMinute, proposal.ToMinute).ShouldBe((moved, CalendarDayKind.Special, SessionClock.OpenMinute, SessionClock.CloseMinute));
        proposal.Note.ShouldBe("Seen in the stored bars: 214 of 214 instruments have bars from 09:15 to 15:30.");
    }

    [Fact]
    public void A_confirmed_holiday_with_stray_bars_from_too_few_instruments_stays_a_holiday_and_a_regular_weekday_is_nothing_special()
    {
        var moved = D(2024, 10, 22);
        var holidays = new HashSet<DateOnly> { moved };
        var strays = new PresenceBuilder().Weekdays(Instruments, First, Last, (i, d) => d != moved || i < 10).Build(); // 10 of 214 is below 5 %
        var strayWindows = new Dictionary<DateOnly, ObservedWindow> { [moved] = new(10, SessionClock.OpenMinute, SessionClock.CloseMinute) };
        var ordinary = Universe().Build();
        var ordinaryWindows = new Dictionary<DateOnly, ObservedWindow> { [moved] = new(Instruments, SessionClock.OpenMinute, SessionClock.CloseMinute) };

        ObservedProposals.Build(strays, strayWindows, holidays).ShouldHaveSingleItem().Kind.ShouldBe(CalendarDayKind.Holiday); // the bars agree with the list
        ObservedProposals.WindowCandidates(strays, holidays).ShouldBeEmpty();
        ObservedProposals.Build(ordinary, ordinaryWindows).ShouldBeEmpty(); // not a holiday: a regular session is nothing special
    }

    [Fact]
    public void The_muhurat_evening_is_a_special_session_and_not_a_holiday()
    {
        var muhurat = D(2024, 11, 1);
        var presence = new PresenceBuilder().Weekdays(Instruments, First, Last, bars: d => d == muhurat ? 60 : 375).Build();
        var windows = new Dictionary<DateOnly, ObservedWindow> { [muhurat] = new(Instruments, 18 * 60 + 15, 19 * 60 + 15) };

        ObservedProposals.WindowCandidates(presence).ShouldBe([muhurat]);
        var proposal = Build(presence, windows).ShouldHaveSingleItem();

        (proposal.Date, proposal.Kind, proposal.FromMinute, proposal.ToMinute).ShouldBe((muhurat, CalendarDayKind.Special, 1095, 1155));
        proposal.Note.ShouldBe("Seen in the stored bars: 214 of 214 instruments have bars from 18:15 to 19:15.");
    }

    [Fact]
    public void Two_saturdays_with_sixty_bars_are_special_sessions()
    {
        var first = D(2024, 3, 2);
        var second = D(2024, 5, 18);
        var presence = new PresenceBuilder()
            .Weekdays(Instruments, D(2024, 2, 26), D(2024, 5, 24))
            .On(first, Instruments, 60)
            .On(second, Instruments, 60)
            .Build();
        var windows = new Dictionary<DateOnly, ObservedWindow>
        {
            [first] = new(Instruments, 9 * 60 + 15, 10 * 60 + 15),
            [second] = new(Instruments, 9 * 60 + 15, 10 * 60 + 15),
        };

        ObservedProposals.WindowCandidates(presence).ShouldBe([second, first]); // newest first
        var proposals = Build(presence, windows);

        proposals.Select(p => (p.Date, p.Kind, p.FromMinute, p.ToMinute)).ShouldBe([(first, CalendarDayKind.Special, 555, 615), (second, CalendarDayKind.Special, 555, 615)]);
    }

    [Fact]
    public void A_weekend_with_bars_from_too_few_instruments_is_not_proposed()
    {
        var saturday = D(2024, 10, 19);
        var presence = Universe().On(saturday, 10, 60).Build(); // quorum is 11 of 214
        var windows = new Dictionary<DateOnly, ObservedWindow> { [saturday] = new(10, 555, 615) };

        ObservedProposals.WindowCandidates(presence).ShouldBeEmpty();
        Build(presence, windows).ShouldBeEmpty();
    }

    [Fact]
    public void A_single_instrument_universe_needs_one_instrument_for_a_special_session()
    {
        var saturday = D(2024, 10, 19);
        var presence = new PresenceBuilder().Weekdays(1, First, Last).On(saturday, 1, 60).Build();
        var windows = new Dictionary<DateOnly, ObservedWindow> { [saturday] = new(1, 555, 615) };

        Build(presence, windows).ShouldHaveSingleItem().Kind.ShouldBe(CalendarDayKind.Special);
    }

    [Fact]
    public void A_weekday_with_a_partial_regular_session_is_not_special()
    {
        var partial = D(2024, 10, 23);
        var presence = new PresenceBuilder().Weekdays(Instruments, First, Last, bars: d => d == partial ? 10 : 375).Build();
        var windows = new Dictionary<DateOnly, ObservedWindow> { [partial] = new(Instruments, 9 * 60 + 15, 9 * 60 + 25) };

        ObservedProposals.WindowCandidates(presence).ShouldBe([partial]); // its window has to be looked at…
        Build(presence, windows).ShouldBeEmpty(); // …and is an ordinary morning
    }

    [Fact]
    public void A_special_date_whose_window_was_not_read_is_not_proposed()
    {
        var presence = Universe().On(D(2024, 10, 19), Instruments, 60).Build();

        Build(presence).ShouldBeEmpty();
    }

    [Fact]
    public void An_unusable_window_is_not_proposed()
    {
        var saturday = D(2024, 10, 19);
        var presence = Universe().On(saturday, Instruments, 60).Build();

        Build(presence, new Dictionary<DateOnly, ObservedWindow> { [saturday] = new(Instruments, 700, 700) }).ShouldBeEmpty();
    }

    [Fact]
    public void Only_the_newest_two_hundred_dates_have_their_window_read()
    {
        var presence = new PresenceBuilder().Weekdays(1, new DateOnly(2020, 1, 1), new DateOnly(2024, 12, 31), bars: _ => 60).Build();

        var candidates = ObservedProposals.WindowCandidates(presence);

        candidates.Count.ShouldBe(ObservedProposals.MaxWindowDates);
        candidates[0].ShouldBe(new DateOnly(2024, 12, 31));
    }

    [Fact]
    public void Windows_are_only_read_for_dates_that_can_be_special()
    {
        var saturday = D(2024, 10, 19);
        var evening = D(2024, 10, 22);
        var presence = new PresenceBuilder().Weekdays(Instruments, First, Last, bars: d => d == evening ? 60 : 375).On(saturday, Instruments, 60).Build();

        ObservedProposals.WindowCandidates(presence).ShouldBe([evening, saturday]);
    }
}
