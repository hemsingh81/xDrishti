using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Market.Bars.Gaps;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Tests.Market.Bars;

/// <summary>Gap planning and the fill/unfillable decisions (ADR 0017 §3) on in-memory ports and a fake clock: 15:50 India time on Monday 2026-10-12, the session of that day has closed.</summary>
public sealed class GapBackfillTests
{
    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    private static readonly DateTimeOffset Now = new(2026, 10, 12, 15, 50, 0, Ist);

    private static readonly DateOnly Monday = new(2026, 10, 12);

    private static readonly DateOnly Friday = new(2026, 10, 9);

    private static readonly DateOnly Thursday = new(2026, 10, 8);

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly FakeBarStore _store = new();
    private readonly FakeBarGaps _gaps = new();
    private readonly FakeDataSettings _settings = new();
    private readonly FakeBarDayCounts _counts;
    private readonly Guid _id = Guid.CreateVersion7();

    public GapBackfillTests() => _counts = new FakeBarDayCounts(_store);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private GapBackfill Backfill => new(_counts, _gaps, _settings, _clock);

    private static TradingDay Holiday(DateOnly date) => TradingDay.Holiday(date, CalendarSource.Manual, CalendarStatus.Confirmed, null, Now);

    private FetchTarget Target(DateTimeOffset? lastBar = null, DateTimeOffset? firstBar = null) =>
        new(_id, "NSE:ABB", "share", "13", "NSE_EQ", lastBar ?? new DateTimeOffset(2026, 10, 12, 15, 29, 0, Ist), firstBar ?? new DateTimeOffset(2026, 8, 3, 9, 15, 0, Ist));

    /// <summary>Every session of the lookback complete (the calendar's expected minutes), then the given days changed.</summary>
    private void Complete(TradingCalendar calendar, params (DateOnly Day, int Count)[] changes)
    {
        var counts = new Dictionary<DateOnly, int>();
        for (var day = Monday; day > Monday.AddDays(-60); day = day.AddDays(-1))
        {
            counts[day] = calendar.ExpectedMinutes(day);
        }

        foreach (var (day, count) in changes)
        {
            counts[day] = count;
        }

        _counts.Preset[_id] = counts;
    }

    [Fact]
    public async Task A_partial_session_older_than_the_ordinary_window_is_recorded_as_open_and_gets_its_own_window()
    {
        var calendar = new TradingCalendar();
        Complete(calendar, (Friday, 200));

        var plan = await Backfill.PlanAsync(calendar, Target(), retryUnfillable: false, Ct);

        var gap = _gaps.Rows.Values.ShouldHaveSingleItem();
        (gap.Date, gap.Expected, gap.Found, gap.Status, gap.Attempts).ShouldBe((Friday, 375, 200, GapStatus.Open, 0));
        plan.Windows.ShouldBe([new FetchWindow(new DateTimeOffset(2026, 10, 9, 0, 0, 0, Ist), new DateTimeOffset(2026, 10, 10, 0, 0, 0, Ist))]);
        plan.Items.ShouldHaveSingleItem().Refetch.ShouldBeTrue();
        plan.Deferred.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_holiday_is_not_a_gap_even_though_it_has_no_bars()
    {
        var calendar = new TradingCalendar([Holiday(Thursday)]);
        Complete(calendar);

        var plan = await Backfill.PlanAsync(calendar, Target(), false, Ct);

        _gaps.Rows.ShouldBeEmpty();
        plan.Items.ShouldBeEmpty();
        plan.Windows.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_weekend_is_not_a_gap()
    {
        var calendar = new TradingCalendar();
        Complete(calendar);

        await Backfill.PlanAsync(calendar, Target(), false, Ct);

        _gaps.Rows.Keys.ShouldNotContain(k => k.Date.DayOfWeek == DayOfWeek.Saturday || k.Date.DayOfWeek == DayOfWeek.Sunday);
        _gaps.Rows.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(60, false)]
    [InlineData(59, true)]
    [InlineData(0, true)]
    public async Task A_short_special_session_is_complete_with_the_bars_of_its_own_length(int found, bool gap)
    {
        var saturday = new DateOnly(2026, 10, 3);
        var calendar = new TradingCalendar([TradingDay.Special(saturday, 10 * 60, 11 * 60, CalendarSource.Manual, CalendarStatus.Confirmed, null, Now)]);
        Complete(calendar, (saturday, found));

        await Backfill.PlanAsync(calendar, Target(), false, Ct);

        if (gap)
        {
            var recorded = _gaps.Rows.Values.ShouldHaveSingleItem();
            (recorded.Date, recorded.Expected, recorded.Found).ShouldBe((saturday, 60, found));
        }
        else
        {
            _gaps.Rows.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Sessions_before_the_first_bar_and_the_first_day_itself_are_not_gaps()
    {
        var calendar = new TradingCalendar();
        Complete(calendar, (new DateOnly(2026, 10, 7), 0), (new DateOnly(2026, 10, 6), 0), (new DateOnly(2026, 10, 5), 0));

        var plan = await Backfill.PlanAsync(calendar, Target(firstBar: new DateTimeOffset(2026, 10, 7, 11, 0, 0, Ist)), false, Ct);

        _gaps.Rows.ShouldBeEmpty();
        plan.Windows.ShouldBeEmpty();
    }

    [Fact]
    public async Task Only_the_latest_sessions_of_the_lookback_are_examined()
    {
        _settings.GapLookbackSessions = 3; // Oct 12, 9, 8
        var calendar = new TradingCalendar();
        Complete(calendar, (new DateOnly(2026, 10, 7), 10), (Thursday, 10));

        await Backfill.PlanAsync(calendar, Target(), false, Ct);

        _gaps.Rows.Keys.Select(k => k.Date).ShouldBe([Thursday]);
        _counts.Asked.ShouldHaveSingleItem().ShouldBe((_id, Thursday, Monday)); // one grouped question for the whole lookback
    }

    [Fact]
    public async Task An_instrument_without_bars_is_not_examined_at_all()
    {
        var target = new FetchTarget(_id, "NSE:NEW", "share", "13", "NSE_EQ", null, null);

        var plan = await Backfill.PlanAsync(new TradingCalendar(), target, false, Ct);

        plan.Items.ShouldBeEmpty();
        _counts.Asked.ShouldBeEmpty();
        _gaps.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_partial_session_inside_the_ordinary_window_is_not_recorded_before_the_fetch_but_checked_after_it()
    {
        var calendar = new TradingCalendar();
        Complete(calendar, (Monday, 100)); // today's fetch has not happened yet
        var plan = await Backfill.PlanAsync(calendar, Target(lastBar: new DateTimeOffset(2026, 10, 9, 15, 29, 0, Ist)), false, Ct);

        _gaps.Rows.ShouldBeEmpty();
        plan.Windows.ShouldBeEmpty();
        plan.Deferred.ShouldBe([new DeferredDay(Monday, 375, 100)]);

        _counts.Preset[_id][Monday] = 375; // the ordinary fetch completed it
        var outcome = await Backfill.CompleteAsync(_id, plan, Ct);

        _gaps.Rows.ShouldBeEmpty();
        (outcome.Found, outcome.Filled, outcome.Unfillable, outcome.StillOpen).ShouldBe((0, 0, 0, 0));
    }

    [Fact]
    public async Task A_deferred_session_that_the_fetch_left_incomplete_becomes_a_gap_that_is_open_when_the_fetch_added_bars_and_unfillable_when_it_did_not()
    {
        var calendar = new TradingCalendar();
        Complete(calendar, (Monday, 100), (Friday, 50));
        var plan = await Backfill.PlanAsync(calendar, Target(lastBar: new DateTimeOffset(2026, 10, 8, 15, 29, 0, Ist)), false, Ct);
        plan.Deferred.Select(d => d.Date).ShouldBe([Friday, Monday]);

        _counts.Preset[_id][Monday] = 300; // more, not all
        _counts.Preset[_id][Friday] = 50;  // nothing more
        var outcome = await Backfill.CompleteAsync(_id, plan, Ct);

        (outcome.Found, outcome.Filled, outcome.Unfillable, outcome.StillOpen).ShouldBe((2, 0, 1, 1));
        var monday = _gaps.Rows[(_id, Monday)];
        (monday.Status, monday.Found, monday.Attempts).ShouldBe((GapStatus.Open, 300, 1));
        var friday = _gaps.Rows[(_id, Friday)];
        (friday.Status, friday.Found, friday.Attempts).ShouldBe((GapStatus.Unfillable, 50, 1));
    }

    [Fact]
    public async Task A_refetch_that_completes_the_session_fills_the_gap()
    {
        var calendar = new TradingCalendar();
        Complete(calendar, (Friday, 200));
        var plan = await Backfill.PlanAsync(calendar, Target(), false, Ct);

        _counts.Preset[_id][Friday] = 375;
        var outcome = await Backfill.CompleteAsync(_id, plan, Ct);

        (outcome.Found, outcome.Filled, outcome.Unfillable, outcome.StillOpen).ShouldBe((1, 1, 0, 0));
        var gap = _gaps.Rows[(_id, Friday)];
        (gap.Status, gap.Found, gap.Attempts, gap.LastAttemptAt).ShouldBe((GapStatus.Filled, 375, 1, Now));
    }

    [Fact]
    public async Task A_refetch_that_adds_nothing_makes_the_session_unfillable_and_scheduled_runs_leave_it_alone()
    {
        var calendar = new TradingCalendar();
        Complete(calendar, (Friday, 200));
        var plan = await Backfill.PlanAsync(calendar, Target(), false, Ct);
        var outcome = await Backfill.CompleteAsync(_id, plan, Ct);
        (outcome.Found, outcome.Unfillable).ShouldBe((1, 1));
        _gaps.Rows[(_id, Friday)].Status.ShouldBe(GapStatus.Unfillable);

        var next = await Backfill.PlanAsync(calendar, Target(), retryUnfillable: false, Ct);

        next.Windows.ShouldBeEmpty();
        var item = next.Items.ShouldHaveSingleItem();
        (item.Gap.Status, item.Refetch).ShouldBe((GapStatus.Unfillable, false));
        (await Backfill.CompleteAsync(_id, next, Ct)).ShouldBe(GapOutcome.None);
        _gaps.Rows[(_id, Friday)].Attempts.ShouldBe(1);
    }

    [Fact]
    public async Task Re_fetching_gaps_asks_again_for_an_unfillable_session_and_can_fill_it()
    {
        var calendar = new TradingCalendar();
        Complete(calendar, (Friday, 200));
        await Backfill.CompleteAsync(_id, await Backfill.PlanAsync(calendar, Target(), false, Ct), Ct); // unfillable

        var retry = await Backfill.PlanAsync(calendar, Target(), retryUnfillable: true, Ct);

        retry.Windows.ShouldHaveSingleItem().From.ShouldBe(new DateTimeOffset(2026, 10, 9, 0, 0, 0, Ist));
        retry.Items.ShouldHaveSingleItem().Refetch.ShouldBeTrue();
        _counts.Preset[_id][Friday] = 375;
        var outcome = await Backfill.CompleteAsync(_id, retry, Ct);
        (outcome.Filled, _gaps.Rows[(_id, Friday)].Status, _gaps.Rows[(_id, Friday)].Attempts).ShouldBe((1, GapStatus.Filled, 2));
    }

    [Fact]
    public async Task An_open_gap_that_got_complete_by_other_means_is_filled_without_a_fetch()
    {
        var calendar = new TradingCalendar();
        Complete(calendar, (Friday, 200));
        await Backfill.PlanAsync(calendar, Target(), false, Ct);
        _counts.Preset[_id][Friday] = 375; // a CSV file was loaded meanwhile

        var plan = await Backfill.PlanAsync(calendar, Target(), false, Ct);

        plan.Items.ShouldBeEmpty();
        plan.Windows.ShouldBeEmpty();
        _gaps.Rows[(_id, Friday)].Status.ShouldBe(GapStatus.Filled);
    }

    [Fact]
    public async Task A_filled_session_that_lost_bars_is_a_gap_again()
    {
        var calendar = new TradingCalendar();
        Complete(calendar, (Friday, 200));
        var first = await Backfill.PlanAsync(calendar, Target(), false, Ct);
        _counts.Preset[_id][Friday] = 375;
        await Backfill.CompleteAsync(_id, first, Ct);
        _counts.Preset[_id][Friday] = 120; // an undo removed bars

        var plan = await Backfill.PlanAsync(calendar, Target(), false, Ct);

        var gap = _gaps.Rows[(_id, Friday)];
        (gap.Status, gap.Found, gap.Attempts).ShouldBe((GapStatus.Open, 120, 0));
        plan.Windows.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task An_open_gap_older_than_the_lookback_is_still_retried_until_it_is_filled_or_unfillable()
    {
        _settings.GapLookbackSessions = 3;
        var calendar = new TradingCalendar();
        var old = new DateOnly(2026, 9, 21);
        Complete(calendar, (old, 100));
        _gaps.Rows[(_id, old)] = BarGap.Detect(_id, old, 375, 100, Now.AddDays(-20));

        var plan = await Backfill.PlanAsync(calendar, Target(), false, Ct);

        plan.Windows.ShouldHaveSingleItem().From.ShouldBe(new DateTimeOffset(2026, 9, 21, 0, 0, 0, Ist));
        await Backfill.CompleteAsync(_id, plan, Ct);
        _gaps.Rows[(_id, old)].Status.ShouldBe(GapStatus.Unfillable);
    }

    [Fact]
    public async Task Gap_sessions_that_follow_each_other_are_fetched_in_one_window_and_the_others_in_their_own()
    {
        var calendar = new TradingCalendar();
        Complete(calendar, (new DateOnly(2026, 10, 5), 10), (new DateOnly(2026, 10, 6), 10), (new DateOnly(2026, 10, 7), 10), (new DateOnly(2026, 10, 1), 10));

        var plan = await Backfill.PlanAsync(calendar, Target(), false, Ct);

        plan.Windows.Select(w => (w.From.Day, w.To.Day)).ShouldBe([(1, 2), (5, 8)]);
    }

    [Fact]
    public async Task A_failure_while_counting_is_not_swallowed_here_so_the_phase_can_report_it()
    {
        var failing = new ThrowingCounts();
        var backfill = new GapBackfill(failing, _gaps, _settings, _clock);

        await Should.ThrowAsync<InvalidOperationException>(async () => await backfill.PlanAsync(new TradingCalendar(), Target(), false, Ct));
    }

    private sealed class ThrowingCounts : IBarDayCounts
    {
        public ValueTask<IReadOnlyDictionary<DateOnly, int>> CountAsync(Guid instrumentId, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("database down");
    }
}
