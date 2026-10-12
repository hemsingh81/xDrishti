using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Market.Bars;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Tests.Market.Bars;

public sealed class DataQueryHandlersTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 6, 30, 0, TimeSpan.Zero); // Monday 12:00 India

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly IBarQueries _queries = Substitute.For<IBarQueries>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_list_asks_for_the_checked_search_with_the_last_closed_session()
    {
        DataInstrumentSearch? seen = null;
        DateTimeOffset? start = null;
        DateOnly? expected = null;
        _queries.ListInstrumentsAsync(Arg.Do<DataInstrumentSearch>(s => seen = s), Arg.Do<DateTimeOffset>(d => start = d), Arg.Do<DateOnly>(d => expected = d), Arg.Any<CancellationToken>())
            .Returns(new DataInstrumentPage([], 2, 10, 0));

        var result = await new ListDataInstrumentsHandler(_queries, _clock)
            .HandleAsync(new ListDataInstrumentsQuery(new DataInstrumentRequest("  abb ", "Behind", "-lastBar", 2, 10)), Ct);

        result.IsSuccess.ShouldBeTrue();
        seen.ShouldBe(new DataInstrumentSearch("abb", DataInstrumentStatus.Behind, DataSortField.LastBar, true, 2, 10));
        expected.ShouldBe(new DateOnly(2026, 10, 9));
        start.ShouldBe(new DateTimeOffset(2026, 10, 9, 0, 0, 0, TimeSpan.FromMinutes(330)));
        start!.Value.Offset.ShouldBe(TimeSpan.FromMinutes(330));
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

        var result = await new GetInstrumentBarsHandler(_queries).HandleAsync(new GetInstrumentBarsQuery(id, null, null), Ct);

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

        var result = await new GetInstrumentBarsHandler(_queries).HandleAsync(new GetInstrumentBarsQuery(id, code, 5), Ct);

        result.Value.Timeframe.ShouldBe(code);
    }

    [Theory]
    [InlineData("2h", 10, "timeframe")]
    [InlineData("1d", 10, "timeframe")]
    [InlineData("1D", 0, "take")]
    [InlineData("1D", 1001, "take")]
    public async Task A_bad_timeframe_or_take_is_refused_before_the_instrument_is_looked_up(string timeframe, int take, string field)
    {
        var result = await new GetInstrumentBarsHandler(_queries).HandleAsync(new GetInstrumentBarsQuery(Guid.NewGuid(), timeframe, take), Ct);

        result.Error.Code.ShouldBe("data.invalid");
        result.Error.Fields!.ShouldHaveSingleItem().Field.ShouldBe(field);
        await _queries.DidNotReceiveWithAnyArgs().FindInstrumentAsync(default, Ct);
    }

    [Fact]
    public async Task Bars_of_an_unknown_instrument_are_not_found()
    {
        _queries.FindInstrumentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((BarsInstrument?)null);

        var result = await new GetInstrumentBarsHandler(_queries).HandleAsync(new GetInstrumentBarsQuery(Guid.NewGuid(), "1D", 10), Ct);

        result.Error.Code.ShouldBe("data.instrument_not_found");
    }

    [Fact]
    public async Task The_overview_counts_weekday_sessions_behind_and_lists_all_eight_timeframes_even_when_empty()
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
            new FakeDataJobStore(),
            _clock);

        var overview = await handler.HandleAsync(new GetDataOverviewQuery(), Ct);

        (overview.Bars.DaysBehind, overview.Bars.Stale, overview.Bars.ExpectedSession, overview.Bars.InstrumentsBehind, overview.Bars.InstrumentsEmpty).ShouldBe((2, true, new DateOnly(2026, 10, 9), 2, 1));
        overview.Aggregates.Timeframes.Select(t => (t.Timeframe, t.Bars)).ShouldBe([("5m", 0L), ("10m", 0L), ("15m", 0L), ("30m", 0L), ("1h", 0L), ("1D", 20L), ("1W", 0L), ("1M", 0L)]);
        overview.Aggregates.InstrumentsPending.ShouldBe(1);
        (overview.Csv.FolderExists, overview.Csv.FilesTotal).ShouldBe((false, 0));
        (overview.Dhan.CanFetch, overview.Dhan.Account!.Name, overview.Dhan.Reason).ShouldBe((true, "Primary", null));
    }

    [Fact]
    public async Task A_folder_that_cannot_be_read_still_gives_an_overview_with_no_files()
    {
        _queries.GetBarTotalsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new BarTotals(0, 0, null, null, 0, 0));
        _queries.GetAggregateTotalsAsync(Arg.Any<CancellationToken>()).Returns(new AggregateTotals([], null, 0));
        var folder = new FakeCsvFolder { ListFailure = new UnauthorizedAccessException("denied") };
        var handler = new GetDataOverviewHandler(folder, new FakeCsvFileLog(), new FakeIntradaySource(), FakeAccountStatus.Connected(), new FakeDataSettings(), _queries, new FakeDataJobStore(), _clock);

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
        var handler = new GetDataOverviewHandler(new FakeCsvFolder(), new FakeCsvFileLog(), new FakeIntradaySource(), FakeAccountStatus.Connected(), new FakeDataSettings(), _queries, jobs, _clock);

        var overview = await handler.HandleAsync(new GetDataOverviewQuery(), Ct);

        overview.Dhan.LastFetchBars.ShouldBe(321);
        overview.Dhan.LastFetchAt.ShouldBe(Now.AddDays(-2).AddMinutes(4));
        overview.LastJobs.Dhan!.Id.ShouldBe(skipped.Id);
        overview.LastJobs.Csv.ShouldBeNull();
        overview.RunningJob.ShouldBeNull();
        overview.Bars.DaysBehind.ShouldBeNull();
        overview.Bars.Stale.ShouldBeFalse();
    }
}
