using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Replay;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;

namespace XDrishti.Simulation.Tests.Replay;

/// <summary>Criterion 4 of P3-04: the session table. Each case is classified, an excluded session is not fed, has a note, and bars read = bars fed + bars excluded.</summary>
public sealed class SessionTests
{
    private static readonly PreparedStrategy Bare = Rep.Prepare(OrbV1.Create());

    private static readonly DateOnly Monday = ReplayDays.D1;

    private static readonly DateOnly Tuesday = ReplayDays.D2;

    private static readonly DateOnly Wednesday = ReplayDays.D3;

    private static readonly DateOnly Friday = ReplayDays.D5;

    private static readonly DateOnly Saturday = new(2026, 3, 7);

    private static readonly DateOnly Sunday = new(2026, 3, 8);

    /// <summary>One row of the session table: the calendar, the options, the bars and what the classification must say (<c>Exclusion</c> null: the session runs).</summary>
    private sealed record Case(string Name, Func<Bar[]> Bars, DateOnly Date, SessionExclusion? Exclusion, TradingCalendar? Calendar = null, ReplayOptions? Options = null, int InWindow = 0, int Outside = 0, int Expected = 375);

    private static readonly Case[] Table =
    [
        new("monday 375 bars", () => Dense(Monday, 375), Monday, null, InWindow: 375),
        new("338 bars (90.13 %)", () => Dense(Monday, 338), Monday, null, InWindow: 338),
        new("337 bars (89.87 %)", () => Dense(Monday, 337), Monday, SessionExclusion.ShortSession, InWindow: 337),
        new("357 bars at 0.95", () => Dense(Monday, 357), Monday, null, Options: new ReplayOptions(0.95), InWindow: 357),
        new("356 bars at 0.95", () => Dense(Monday, 356), Monday, SessionExclusion.ShortSession, Options: new ReplayOptions(0.95), InWindow: 356),
        new("300 bars at 0.80 (exactly the threshold runs)", () => Dense(Monday, 300), Monday, null, Options: new ReplayOptions(0.80), InWindow: 300),
        new("299 bars at 0.80", () => Dense(Monday, 299), Monday, SessionExclusion.ShortSession, Options: new ReplayOptions(0.80), InWindow: 299),
        new("374 bars, 15:29 missing", () => Dense(Monday, 374), Monday, null, InWindow: 374),
        new("tuesday 60 bars 13:45 (the 2025-10-21 shape)", () => ReplayDays.Minutes(Tuesday, "13:45", 60), Tuesday, SessionExclusion.ShortSession, InWindow: 60),
        new("friday 60 bars 18:00 (the 2024-11-01 shape)", () => ReplayDays.Minutes(Friday, "18:00", 60), Friday, SessionExclusion.ShortSession, InWindow: 0),
        new("saturday 105 bars (the 2024-05-18 shape)", () => ReplayDays.Minutes(Saturday, "09:15", 105), Saturday, SessionExclusion.ClosedDay),
        new("saturday 375 bars (the 2025-02-01 shape)", () => Dense(Saturday, 375), Saturday, SessionExclusion.ClosedDay),
        new("sunday 375 bars (the 2026-02-01 shape)", () => Dense(Sunday, 375), Sunday, SessionExclusion.ClosedDay),
        new("special saturday 09:15 to 15:30, the same 375 bars", () => Dense(Saturday, 375), Saturday, null, Calendar: Rep.WithSpecial(Saturday, 9 * 60 + 15, 15 * 60 + 30), InWindow: 375),
        new("special saturday 09:15 to 12:30, the 105 bars", () => ReplayDays.Minutes(Saturday, "09:15", 105), Saturday, SessionExclusion.SpecialSession, Calendar: Rep.WithSpecial(Saturday, 9 * 60 + 15, 12 * 60 + 30), InWindow: 105, Expected: 195),
        new("special tuesday 13:45 to 14:45, the same 60 bars", () => ReplayDays.Minutes(Tuesday, "13:45", 60), Tuesday, SessionExclusion.SpecialSession, Calendar: Rep.WithSpecial(Tuesday, 13 * 60 + 45, 14 * 60 + 45), InWindow: 60, Expected: 60),
        new("holiday on a wednesday, 375 bars", () => Dense(Wednesday, 375), Wednesday, SessionExclusion.ClosedDay, Calendar: Rep.WithHoliday(Wednesday)),
        new("monday 375 bars and 5 bars 18:00 to 18:04", () => ReplayDays.Join(Dense(Monday, 375), ReplayDays.Minutes(Monday, "18:00", 5)), Monday, null, InWindow: 375, Outside: 5),
    ];

    public static TheoryData<string> Names => [.. Table.Select(c => c.Name)];

    private static Bar[] Dense(DateOnly date, int count) => ReplayDays.Minutes(date, "09:15", count);

    [Theory]
    [MemberData(nameof(Names))]
    public void A_session_runs_or_is_excluded_with_exactly_the_reason_of_the_table(string name)
    {
        var row = Table.Single(c => c.Name == name);
        var bars = row.Bars();

        var record = Rep.Replay(Bare, bars, row.Date, row.Date, calendar: row.Calendar, options: row.Options).Record;

        record.BarsRead.ShouldBe(bars.Length);
        (record.BarsFed + record.BarsExcluded).ShouldBe(record.BarsRead, "bars read = bars fed + bars excluded");
        record.BarsOutsideSession.ShouldBe(row.Outside);
        if (row.Exclusion is not { } reason)
        {
            record.Sessions.Ran.ShouldBe(1);
            record.Sessions.Excluded.ShouldBe(0);
            record.Notes.ShouldBeEmpty();
            record.BarsFed.ShouldBe(row.InWindow);
            return;
        }

        record.Sessions.Ran.ShouldBe(0);
        record.Sessions.Of(reason).ShouldBe(1);
        record.Sessions.Excluded.ShouldBe(1);
        record.BarsFed.ShouldBe(0, "an excluded session is not fed");
        record.BarsExcluded.ShouldBe(bars.Length);
        record.Skipped.ShouldBe(SkipReason.NoRunnableSessions);
        var note = record.Notes.ShouldHaveSingleItem();
        note.Date.ShouldBe(row.Date);
        note.Reason.ShouldBe(reason);
        note.BarsInWindow.ShouldBe(reason == SessionExclusion.ClosedDay ? 0 : row.InWindow);
        note.ExpectedMinutes.ShouldBe(reason == SessionExclusion.ClosedDay ? 0 : row.Expected);
    }

    [Fact]
    public void A_trading_day_between_two_bar_dates_with_no_bar_is_a_missing_session_with_a_note_and_nothing_is_fed()
    {
        var tuesdayAfter = new DateOnly(2026, 3, 10);
        var bars = ReplayDays.Join(Dense(Friday, 375), Dense(tuesdayAfter, 375));

        var record = Rep.Replay(Bare, bars, Friday, tuesdayAfter).Record;

        record.Sessions.Ran.ShouldBe(2);
        record.Sessions.MissingSession.ShouldBe(1, "Monday 2026-03-09; the weekend between Friday and Monday is not a trading day");
        record.Notes.ShouldHaveSingleItem().ShouldBe(new SessionNote(new DateOnly(2026, 3, 9), SessionExclusion.MissingSession, 0, 0));
        record.BarsFed.ShouldBe(750);
        record.BarsExcluded.ShouldBe(0);
    }

    [Fact]
    public void A_session_of_374_bars_runs_and_its_last_bucket_closes_at_the_first_minute_of_the_next_session_as_an_incomplete_bar()
    {
        var bars = ReplayDays.Join(Dense(Monday, 374), Dense(Tuesday, 375));

        var record = Rep.Replay(Bare, bars, Monday, Tuesday).Record;

        record.Sessions.Ran.ShouldBe(2);
        record.Scans[0].IncompleteBarsConsumed.ShouldBeGreaterThan(0);
        record.Scans[0].StaleBucketSteps.ShouldBeGreaterThan(0);
        record.Scans[0].IgnoredBars.ShouldBe(0);
    }

    [Fact]
    public void After_an_excluded_session_the_previous_day_values_and_the_discontinuity_check_read_the_previous_session_that_ran()
    {
        // Friday flat at 99.00 runs, Saturday at 150.00 is a closed day (excluded), Monday opens at 99.00 again: compared with the Saturday close the open would be a 34 % move and flagged.
        var bars = ReplayDays.Join(Dense(Friday, 375), ReplayDays.Minutes(Saturday, "09:15", 375, 150.0), ReplayDays.Minutes(new DateOnly(2026, 3, 9), "09:15", 375));
        var reads = Rep.Prepare(TestStrategies.WithBuy(OrbV1.Create(), b => b with { Setup = TestStrategies.All("prev_day_close < 100") }));

        var record = Rep.Replay(reads, bars, Friday, new DateOnly(2026, 3, 9)).Record;

        record.Discontinuities.ShouldBeEmpty();
        record.Sessions.ClosedDay.ShouldBe(1);
        Identities.Of(record.Scans[0], "buy.setup[0]").ShouldBe((9L, 0L, 0L, 9L), "Friday is the first session (prev_day_close not ready: 9 steps); Monday reads Friday's close of 99.00 (true at 9 steps)");
    }

    [Fact]
    public void Bars_outside_the_window_of_a_session_that_runs_are_dropped_and_counted_a_bar_at_1530_and_a_bar_at_0914()
    {
        var bars = ReplayDays.Join(ReplayDays.Minutes(Monday, "09:14", 1), Dense(Monday, 375), ReplayDays.Minutes(Monday, "15:30", 1));

        var record = Rep.Replay(Bare, bars, Monday, Monday).Record;

        record.BarsOutsideSession.ShouldBe(2);
        record.BarsExcluded.ShouldBe(2);
        record.BarsFed.ShouldBe(375);
        record.Scans[0].Bars.ShouldBe(375);
        record.Scans[0].IgnoredBars.ShouldBe(0, "excluded is not ignored");
    }

    [Fact]
    public void A_running_session_whose_first_bar_is_0920_runs_and_zero_volume_bars_are_kept()
    {
        var bars = ReplayDays.Minutes(Monday, "09:20", 370).Select((b, i) => i % 2 == 0 ? b with { Volume = 0 } : b).ToArray();

        var record = Rep.Replay(Bare, bars, Monday, Monday).Record;

        record.Sessions.Ran.ShouldBe(1);
        record.BarsFed.ShouldBe(370);
        record.BarsExcluded.ShouldBe(0);
    }

    [Fact]
    public void More_than_a_hundred_excluded_sessions_keep_a_hundred_notes_and_exact_counts()
    {
        // 120 Saturdays-and-Sundays-free weekdays would run; 130 closed days are weekend dates with one bar each, 70 weeks of Saturday and Sunday.
        var bars = new List<Bar>();
        var date = Monday;
        for (var week = 0; week < 65; week++)
        {
            bars.AddRange(ReplayDays.Minutes(date.AddDays(5), "10:00", 1));
            bars.AddRange(ReplayDays.Minutes(date.AddDays(6), "10:00", 1));
            date = date.AddDays(7);
        }

        var record = Rep.Replay(Bare, bars, Monday, date).Record;

        record.Sessions.ClosedDay.ShouldBe(130);
        record.Notes.Count.ShouldBe(100);
        record.NotesTruncated.ShouldBeTrue();
        record.Notes.Select(n => n.Date).ShouldBe(record.Notes.Select(n => n.Date).Order());
    }

    [Fact]
    public void A_calendar_exception_on_a_day_without_bars_changes_nothing_in_the_record_of_the_instrument()
    {
        var bars = ReplayDays.Join(Dense(Monday, 375), Dense(Tuesday, 375));

        var plain = Rep.Replay(Bare, bars, Monday, Tuesday).Record;
        var withHoliday = Rep.Replay(Bare, bars, Monday, Tuesday, calendar: Rep.WithHoliday(new DateOnly(2026, 3, 20))).Record;

        RunText.Instrument(withHoliday).ShouldBe(RunText.Instrument(plain));
    }

    [Fact]
    public void A_calendar_holiday_between_two_bar_dates_is_not_a_missing_session()
    {
        var bars = ReplayDays.Join(Dense(Monday, 375), Dense(Wednesday, 375));

        var record = Rep.Replay(Bare, bars, Monday, Wednesday, calendar: Rep.WithHoliday(Tuesday)).Record;

        record.Sessions.MissingSession.ShouldBe(0);
        record.Sessions.Ran.ShouldBe(2);
        Rep.Replay(Bare, bars, Monday, Wednesday).Record.Sessions.MissingSession.ShouldBe(1);
    }
}
