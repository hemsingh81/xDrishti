using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Market.Actions;
using XDrishti.Application.Market.Bars;
using XDrishti.Application.Tests.Market.Actions;
using XDrishti.Application.Tests.Market.Calendar;
using XDrishti.Application.Tests.Platform.Services;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Tests.Market.Bars;

public sealed class DataQueryHandlersTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 6, 30, 0, TimeSpan.Zero); // Monday 12:00 India

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly IBarQueries _queries = Substitute.For<IBarQueries>();
    private readonly FixedCalendar _calendar = new();
    private readonly InMemoryCalendarData _days = new();
    private readonly InMemoryActionData _actions = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_list_asks_for_the_checked_search_with_the_last_closed_session()
    {
        DataInstrumentSearch? seen = null;
        DateTimeOffset? start = null;
        DateOnly? expected = null;
        _queries.ListInstrumentsAsync(Arg.Do<DataInstrumentSearch>(s => seen = s), Arg.Do<DateTimeOffset>(d => start = d), Arg.Do<DateOnly>(d => expected = d), _calendar.Calendar, Arg.Any<CancellationToken>())
            .Returns(new DataInstrumentPage([], 2, 10, 0));

        var result = await new ListDataInstrumentsHandler(_queries, _calendar, _clock)
            .HandleAsync(new ListDataInstrumentsQuery(new DataInstrumentRequest("  abb ", "Behind", "-lastBar", 2, 10)), Ct);

        result.IsSuccess.ShouldBeTrue();
        seen.ShouldBe(new DataInstrumentSearch("abb", DataInstrumentStatus.Behind, DataSortField.LastBar, true, 2, 10));
        expected.ShouldBe(new DateOnly(2026, 10, 9));
        start.ShouldBe(new DateTimeOffset(2026, 10, 9, 0, 0, 0, TimeSpan.FromMinutes(330)));
        start!.Value.Offset.ShouldBe(TimeSpan.FromMinutes(330));
    }

    [Fact]
    public void The_gaps_filter_is_passed_on_and_off_by_default()
    {
        ListDataInstrumentsHandler.Check(new DataInstrumentRequest(null, null, null, null, null, true)).Value.HasGaps.ShouldBeTrue();
        ListDataInstrumentsHandler.Check(new DataInstrumentRequest(null, null, null, null, null, false)).Value.HasGaps.ShouldBeFalse();
        ListDataInstrumentsHandler.Check(new DataInstrumentRequest(null, null, null, null, null)).Value.HasGaps.ShouldBeFalse();
    }

    [Fact]
    public void Nothing_given_means_page_one_of_fifty_by_symbol()
    {
        var search = ListDataInstrumentsHandler.Check(new DataInstrumentRequest(null, null, null, null, null));

        search.Value.ShouldBe(new DataInstrumentSearch(null, null, DataSortField.Symbol, false, 1, 50));
    }

    [Theory]
    [InlineData("status", "nope")]
    [InlineData("status", "1")]
    [InlineData("sort", "colour")]
    [InlineData("sort", "-")]
    [InlineData("sort", "2")]
    public void Unknown_status_and_sort_values_are_refused(string field, string value)
    {
        var request = field == "status" ? new DataInstrumentRequest(null, value, null, null, null) : new DataInstrumentRequest(null, null, value, null, null);

        var result = ListDataInstrumentsHandler.Check(request);

        result.Error.Code.ShouldBe("data.invalid");
        result.Error.Fields!.ShouldHaveSingleItem().Field.ShouldBe(field);
    }

    [Fact]
    public void Page_page_size_and_text_are_bounded_and_every_problem_is_reported_at_once()
    {
        var result = ListDataInstrumentsHandler.Check(new DataInstrumentRequest(new string('x', 101), null, null, 0, 201));

        result.Error.Fields!.Select(f => f.Field).ShouldBe(["q", "page", "pageSize"]);
        ListDataInstrumentsHandler.Check(new DataInstrumentRequest("a\u0001b", null, null, null, null)).Error.Fields!.ShouldHaveSingleItem().Field.ShouldBe("q");
        ListDataInstrumentsHandler.Check(new DataInstrumentRequest(null, null, null, DataInstrumentSearch.MaxPage + 1, null)).IsFailure.ShouldBeTrue();
        ListDataInstrumentsHandler.Check(new DataInstrumentRequest(null, null, null, DataInstrumentSearch.MaxPage, DataInstrumentSearch.MaxPageSize)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Bars_default_to_the_day_timeframe_and_a_hundred_bars()
    {
        var id = Guid.NewGuid();
        _queries.FindInstrumentAsync(id, Arg.Any<CancellationToken>()).Returns(new BarsInstrument(id, "NSE:ABB", "ABB Ltd"));
        _queries.ReadBarsAsync(id, Timeframe.D1, 100, Arg.Any<CancellationToken>()).Returns([]);

        var result = await new GetInstrumentBarsHandler(_queries, new AdjustmentFactorsProvider(_actions)).HandleAsync(new GetInstrumentBarsQuery(id, null, null), Ct);

        (result.Value.Timeframe, result.Value.Symbol, result.Value.Name).ShouldBe(("1D", "NSE:ABB", "ABB Ltd"));
    }

    [Theory]
    [InlineData("1m", Timeframe.M1)]
    [InlineData("5m", Timeframe.M5)]
    [InlineData("1h", Timeframe.H1)]
    [InlineData("1W", Timeframe.W1)]
    [InlineData("1M", Timeframe.Mo1)]
    public async Task Each_timeframe_code_reads_its_own_bars(string code, Timeframe timeframe)
    {
        var id = Guid.NewGuid();
        _queries.FindInstrumentAsync(id, Arg.Any<CancellationToken>()).Returns(new BarsInstrument(id, "NSE:ABB", "ABB"));
        _queries.ReadBarsAsync(id, timeframe, 5, Arg.Any<CancellationToken>()).Returns([]);

        var result = await new GetInstrumentBarsHandler(_queries, new AdjustmentFactorsProvider(_actions)).HandleAsync(new GetInstrumentBarsQuery(id, code, 5), Ct);

        result.Value.Timeframe.ShouldBe(code);
    }

    [Theory]
    [InlineData("2h", 10, "timeframe")]
    [InlineData("1d", 10, "timeframe")]
    [InlineData("1D", 0, "take")]
    [InlineData("1D", 1001, "take")]
    public async Task A_bad_timeframe_or_take_is_refused_before_the_instrument_is_looked_up(string timeframe, int take, string field)
    {
        var result = await new GetInstrumentBarsHandler(_queries, new AdjustmentFactorsProvider(_actions)).HandleAsync(new GetInstrumentBarsQuery(Guid.NewGuid(), timeframe, take), Ct);

        result.Error.Code.ShouldBe("data.invalid");
        result.Error.Fields!.ShouldHaveSingleItem().Field.ShouldBe(field);
        await _queries.DidNotReceiveWithAnyArgs().FindInstrumentAsync(default, Ct);
    }

    [Fact]
    public async Task Bars_of_an_unknown_instrument_are_not_found()
    {
        _queries.FindInstrumentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((BarsInstrument?)null);

        var result = await new GetInstrumentBarsHandler(_queries, new AdjustmentFactorsProvider(_actions)).HandleAsync(new GetInstrumentBarsQuery(Guid.NewGuid(), "1D", 10), Ct);

        result.Error.Code.ShouldBe("data.instrument_not_found");
    }

    [Fact]
    public async Task The_overview_counts_trading_days_behind_and_lists_all_eight_timeframes_even_when_empty()
    {
        var last = new DateTimeOffset(2026, 10, 7, 15, 29, 0, TimeSpan.FromMinutes(330));
        _queries.GetBarTotalsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new BarTotals(3, 1000, last.AddDays(-30), last, 2, 1));
        _queries.GetAggregateTotalsAsync(Arg.Any<CancellationToken>()).Returns(new AggregateTotals([new TimeframeStat("1D", 20, last)], Now, 1));
        var handler = new GetDataOverviewHandler(
            new FakeCsvFolder { FolderExists = false },
            new FakeCsvFileLog(),
            new FakeIntradaySource(),
            FakeAccountStatus.Connected(),
            new FakeDataSettings(),
            _queries,
            new FakeBarGaps(),
            new FakeDataJobStore(),
            _calendar,
            _days,
            _actions, ServiceReaders.Schedules(_clock), _clock);

        var overview = await handler.HandleAsync(new GetDataOverviewQuery(), Ct);

        (overview.Bars.DaysBehind, overview.Bars.Stale, overview.Bars.ExpectedSession, overview.Bars.InstrumentsBehind, overview.Bars.InstrumentsEmpty).ShouldBe((2, true, new DateOnly(2026, 10, 9), 2, 1));
        overview.Aggregates.Timeframes.Select(t => (t.Timeframe, t.Bars)).ShouldBe([("5m", 0L), ("10m", 0L), ("15m", 0L), ("30m", 0L), ("1h", 0L), ("1D", 20L), ("1W", 0L), ("1M", 0L)]);
        overview.Aggregates.InstrumentsPending.ShouldBe(1);
        (overview.Csv.FolderExists, overview.Csv.FilesTotal).ShouldBe((false, 0));
        (overview.Dhan.CanFetch, overview.Dhan.Account!.Name, overview.Dhan.Reason).ShouldBe((true, "Primary", null));
    }

    [Fact]
    public async Task A_long_weekend_is_not_behind_and_the_calendar_decides_the_expected_session()
    {
        // Friday 2026-10-09 and Monday 2026-10-12 are holidays; the clock says Monday 12:00. Data to Thursday is current.
        _calendar.Calendar = new TradingCalendar(
        [
            TradingDay.Holiday(new DateOnly(2026, 10, 9), CalendarSource.Manual, CalendarStatus.Confirmed, null, Now),
            TradingDay.Holiday(new DateOnly(2026, 10, 12), CalendarSource.Manual, CalendarStatus.Confirmed, null, Now),
        ]);
        var last = new DateTimeOffset(2026, 10, 8, 15, 29, 0, TimeSpan.FromMinutes(330));
        _queries.GetBarTotalsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new BarTotals(3, 1000, last.AddDays(-30), last, 0, 0));
        _queries.GetAggregateTotalsAsync(Arg.Any<CancellationToken>()).Returns(new AggregateTotals([], null, 0));

        var overview = await Overview().HandleAsync(new GetDataOverviewQuery(), Ct);

        (overview.Bars.ExpectedSession, overview.Bars.DaysBehind, overview.Bars.Stale).ShouldBe((new DateOnly(2026, 10, 8), 0, false));
    }

    [Fact]
    public async Task The_overview_carries_the_next_holiday_and_the_number_of_proposals()
    {
        _days.Days.Add(TradingDay.Holiday(new DateOnly(2026, 10, 20), CalendarSource.Nse, CalendarStatus.Confirmed, "Diwali", Now));
        _days.Days.Add(TradingDay.Holiday(new DateOnly(2026, 10, 2), CalendarSource.Nse, CalendarStatus.Confirmed, "Gandhi Jayanti", Now));
        _days.Days.Add(TradingDay.Holiday(new DateOnly(2026, 10, 15), CalendarSource.Observed, CalendarStatus.Proposed, "seen", Now));
        _days.Days.Add(TradingDay.Special(new DateOnly(2024, 11, 1), 1095, 1155, CalendarSource.Observed, CalendarStatus.Proposed, "seen", Now));
        _calendar.Calendar = new TradingCalendar(_days.Days);
        _queries.GetBarTotalsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new BarTotals(0, 0, null, null, 0, 0));
        _queries.GetAggregateTotalsAsync(Arg.Any<CancellationToken>()).Returns(new AggregateTotals([], null, 0));

        var overview = await Overview().HandleAsync(new GetDataOverviewQuery(), Ct);

        overview.NextHoliday!.Date.ShouldBe(new DateOnly(2026, 10, 20));
        (overview.NextHoliday.Kind, overview.NextHoliday.Note).ShouldBe(("holiday", "Diwali"));
        overview.CalendarProposals.ShouldBe(2);
    }

    [Fact]
    public async Task Without_a_known_holiday_the_next_holiday_is_null()
    {
        _queries.GetBarTotalsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new BarTotals(0, 0, null, null, 0, 0));
        _queries.GetAggregateTotalsAsync(Arg.Any<CancellationToken>()).Returns(new AggregateTotals([], null, 0));

        var overview = await Overview().HandleAsync(new GetDataOverviewQuery(), Ct);

        (overview.NextHoliday, overview.CalendarProposals).ShouldBe((null, 0));
    }

    [Fact]
    public async Task The_instrument_list_gets_the_calendar_and_its_last_closed_session()
    {
        _calendar.Calendar = new TradingCalendar([TradingDay.Holiday(new DateOnly(2026, 10, 9), CalendarSource.Manual, CalendarStatus.Confirmed, null, Now)]);
        DateOnly? expected = null;
        TradingCalendar? seen = null;
        _queries.ListInstrumentsAsync(Arg.Any<DataInstrumentSearch>(), Arg.Any<DateTimeOffset>(), Arg.Do<DateOnly>(d => expected = d), Arg.Do<TradingCalendar>(c => seen = c), Arg.Any<CancellationToken>())
            .Returns(new DataInstrumentPage([], 1, 50, 0));

        await new ListDataInstrumentsHandler(_queries, _calendar, _clock).HandleAsync(new ListDataInstrumentsQuery(new DataInstrumentRequest(null, null, null, null, null)), Ct);

        expected.ShouldBe(new DateOnly(2026, 10, 8)); // Friday is a holiday: Thursday was the last session
        seen.ShouldBeSameAs(_calendar.Calendar);
    }

    private GetDataOverviewHandler Overview() =>
        new(new FakeCsvFolder(), new FakeCsvFileLog(), new FakeIntradaySource(), FakeAccountStatus.Connected(), new FakeDataSettings(), _queries, new FakeBarGaps(), new FakeDataJobStore(), _calendar, _days, _actions, ServiceReaders.Schedules(_clock), _clock);

    [Fact]
    public async Task The_overview_counts_the_suggested_corporate_actions_waiting_for_a_decision()
    {
        EmptyBars(_queries);
        var now = Now;
        _actions.Actions.Add(XDrishti.Domain.Market.Actions.CorporateAction.Suggest(Guid.NewGuid(), new DateOnly(2026, 9, 1), XDrishti.Domain.Market.Actions.CorporateActionKind.Split, 1, 5, null, now));
        var applied = XDrishti.Domain.Market.Actions.CorporateAction.Apply(Guid.NewGuid(), new DateOnly(2026, 9, 2), XDrishti.Domain.Market.Actions.CorporateActionKind.Split, 1, 2, XDrishti.Domain.Market.Actions.CorporateActionSource.Manual, null, "owner", now);
        _actions.Actions.Add(applied);

        var overview = await Overview().HandleAsync(new GetDataOverviewQuery(), Ct);

        overview.PendingCorporateActions.ShouldBe(1);
    }

    [Fact]
    public async Task A_folder_that_cannot_be_read_still_gives_an_overview_with_no_files()
    {
        _queries.GetBarTotalsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new BarTotals(0, 0, null, null, 0, 0));
        _queries.GetAggregateTotalsAsync(Arg.Any<CancellationToken>()).Returns(new AggregateTotals([], null, 0));
        var folder = new FakeCsvFolder { ListFailure = new UnauthorizedAccessException("denied") };
        var handler = new GetDataOverviewHandler(folder, new FakeCsvFileLog(), new FakeIntradaySource(), FakeAccountStatus.Connected(), new FakeDataSettings(), _queries, new FakeBarGaps(), new FakeDataJobStore(), _calendar, _days, _actions, ServiceReaders.Schedules(_clock), _clock);

        var overview = await handler.HandleAsync(new GetDataOverviewQuery(), Ct);

        (overview.Csv.FolderExists, overview.Csv.FilesTotal, overview.Csv.FilesPending).ShouldBe((true, 0, 0));
    }

    [Fact]
    public async Task The_last_fetch_comes_from_the_last_successful_dhan_phase_and_the_last_job_of_a_phase_includes_skipped_ones()
    {
        var jobs = new FakeDataJobStore();
        var fetched = DataJob.Start([DataSteps.Dhan], false, "owner", Now.AddDays(-2));
        fetched.Report(fetched.Dhan with { Status = PhaseStatus.Succeeded, FinishedAt = Now.AddDays(-2).AddMinutes(4), Inserted = 321 });
        fetched.Finish(Now.AddDays(-2).AddMinutes(4));
        var skipped = DataJob.Start([DataSteps.Dhan], false, "owner", Now.AddDays(-1));
        skipped.Report(skipped.Dhan with { Status = PhaseStatus.Skipped, Message = "no account" });
        skipped.Finish(Now.AddDays(-1));
        jobs.Jobs[fetched.Id] = fetched;
        jobs.Jobs[skipped.Id] = skipped;
        _queries.GetBarTotalsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new BarTotals(0, 0, null, null, 0, 0));
        _queries.GetAggregateTotalsAsync(Arg.Any<CancellationToken>()).Returns(new AggregateTotals([], null, 0));
        var handler = new GetDataOverviewHandler(new FakeCsvFolder(), new FakeCsvFileLog(), new FakeIntradaySource(), FakeAccountStatus.Connected(), new FakeDataSettings(), _queries, new FakeBarGaps(), jobs, _calendar, _days, _actions, ServiceReaders.Schedules(_clock), _clock);

        var overview = await handler.HandleAsync(new GetDataOverviewQuery(), Ct);

        overview.Dhan.LastFetchBars.ShouldBe(321);
        overview.Dhan.LastFetchAt.ShouldBe(Now.AddDays(-2).AddMinutes(4));
        overview.LastJobs.Dhan!.Id.ShouldBe(skipped.Id);
        overview.LastJobs.Csv.ShouldBeNull();
        overview.RunningJob.ShouldBeNull();
        overview.Bars.DaysBehind.ShouldBeNull();
        overview.Bars.Stale.ShouldBeFalse();
    }

    private static void EmptyBars(IBarQueries queries)
    {
        queries.GetBarTotalsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new BarTotals(0, 0, null, null, 0, 0));
        queries.GetAggregateTotalsAsync(Arg.Any<CancellationToken>()).Returns(new AggregateTotals([], null, 0));
    }

    [Fact]
    public async Task The_overview_carries_the_gap_counts()
    {
        EmptyBars(_queries);
        var gaps = new FakeBarGaps();
        var id = Guid.NewGuid();
        gaps.Rows[(id, new DateOnly(2026, 10, 7))] = BarGap.Detect(id, new DateOnly(2026, 10, 7), 375, 10, Now.AddDays(-2));
        gaps.Rows[(id, new DateOnly(2026, 10, 8))] = BarGap.Detect(id, new DateOnly(2026, 10, 8), 375, 375, Now);
        var handler = new GetDataOverviewHandler(new FakeCsvFolder(), new FakeCsvFileLog(), new FakeIntradaySource(), FakeAccountStatus.Connected(), new FakeDataSettings(), _queries, gaps, new FakeDataJobStore(), _calendar, _days, _actions, ServiceReaders.Schedules(_clock), _clock);

        var overview = await handler.HandleAsync(new GetDataOverviewQuery(), Ct);

        (overview.Gaps.Open, overview.Gaps.Unfillable, overview.Gaps.Filled, overview.Gaps.LastDetectedAt).ShouldBe((1, 0, 1, Now));
    }

    [Fact]
    public async Task The_overview_shows_the_schedule_the_next_run_and_the_last_automatic_run()
    {
        EmptyBars(_queries);
        var jobs = new FakeDataJobStore();
        var started = new DateTimeOffset(2026, 10, 9, 15, 50, 2, TimeSpan.FromMinutes(330));
        var last = DataJob.Start(["dhan", "aggregate"], false, "system:data-eod", started, DataJobTrigger.Daily);
        last.Fail(started.AddMinutes(2), "Dhan refused the token");
        jobs.Jobs[last.Id] = last;
        var manual = DataJob.Start(["dhan"], false, "owner", started.AddHours(20));
        manual.Finish(started.AddHours(21));
        jobs.Jobs[manual.Id] = manual;
        var handler = new GetDataOverviewHandler(new FakeCsvFolder(), new FakeCsvFileLog(), new FakeIntradaySource(), FakeAccountStatus.Connected(), new FakeDataSettings(), _queries, new FakeBarGaps(), jobs, _calendar, _days, _actions, ServiceReaders.Schedules(_clock), _clock);

        var overview = await handler.HandleAsync(new GetDataOverviewQuery(), Ct);

        (overview.Eod.Enabled, overview.Eod.TimeIst).ShouldBe((true, "15:50"));
        overview.Eod.NextRunAt.ShouldBe(new DateTimeOffset(2026, 10, 12, 15, 50, 0, TimeSpan.FromMinutes(330))); // Monday 12:00 India: today's run is ahead
        var run = overview.Eod.LastRun!;
        (run.JobId, run.StartedAt, run.Status, run.Trigger, run.Message).ShouldBe((last.Id, started, "failed", "daily", "Dhan refused the token"));
    }

    [Fact]
    public async Task With_the_schedule_switched_off_the_overview_has_no_next_run_and_no_last_run_before_the_first()
    {
        EmptyBars(_queries);
        var handler = new GetDataOverviewHandler(
            new FakeCsvFolder(), new FakeCsvFileLog(), new FakeIntradaySource(), FakeAccountStatus.Connected(), new FakeDataSettings(), _queries, new FakeBarGaps(), new FakeDataJobStore(), _calendar, _days, _actions, ServiceReaders.Schedules(_clock, new FakeDataSettings { EodEnabled = false, EodTimeIst = new TimeOnly(16, 5) }), _clock);

        var overview = await handler.HandleAsync(new GetDataOverviewQuery(), Ct);

        (overview.Eod.Enabled, overview.Eod.TimeIst, overview.Eod.NextRunAt, overview.Eod.LastRun).ShouldBe((false, "16:05", null, null));
    }

    [Fact]
    public async Task The_next_run_skips_a_holiday_of_the_calendar()
    {
        EmptyBars(_queries);
        _calendar.Calendar = new TradingCalendar([TradingDay.Holiday(new DateOnly(2026, 10, 12), CalendarSource.Manual, CalendarStatus.Confirmed, null, Now)]);
        var handler = new GetDataOverviewHandler(new FakeCsvFolder(), new FakeCsvFileLog(), new FakeIntradaySource(), FakeAccountStatus.Connected(), new FakeDataSettings(), _queries, new FakeBarGaps(), new FakeDataJobStore(), _calendar, _days, _actions, ServiceReaders.Schedules(_clock, calendar: _calendar), _clock);

        var overview = await handler.HandleAsync(new GetDataOverviewQuery(), Ct);

        overview.Eod.NextRunAt.ShouldBe(new DateTimeOffset(2026, 10, 13, 15, 50, 0, TimeSpan.FromMinutes(330)));
    }

    [Fact]
    public async Task The_overview_reads_the_schedule_the_owner_set_on_the_services_screen_not_the_configuration()
    {
        EmptyBars(_queries);
        var rows = new InMemoryServiceData();
        rows.Store("eod", XDrishti.Application.Tests.Platform.Services.Svc.Settings(Schedule.Daily(new TimeOnly(16, 20)), enabled: true));
        var handler = new GetDataOverviewHandler(new FakeCsvFolder(), new FakeCsvFileLog(), new FakeIntradaySource(), FakeAccountStatus.Connected(), new FakeDataSettings { EodEnabled = false, EodTimeIst = new TimeOnly(9, 0) }, _queries, new FakeBarGaps(), new FakeDataJobStore(), _calendar, _days, _actions, ServiceReaders.Schedules(_clock, new FakeDataSettings { EodEnabled = false, EodTimeIst = new TimeOnly(9, 0) }, rows: rows), _clock);

        var overview = await handler.HandleAsync(new GetDataOverviewQuery(), Ct);

        (overview.Eod.Enabled, overview.Eod.TimeIst, overview.Eod.ScheduleWords).ShouldBe((true, "16:20", "Every trading day at 16:20 IST, never before the session has ended"));
        overview.Eod.NextRunAt.ShouldBe(new DateTimeOffset(2026, 10, 12, 16, 20, 0, TimeSpan.FromMinutes(330)));
    }
}
