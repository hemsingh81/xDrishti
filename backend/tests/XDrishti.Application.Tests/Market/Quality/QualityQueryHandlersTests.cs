using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Market.Quality;
using XDrishti.Application.Market.Quality;
using XDrishti.Application.Tests.Market.Actions;
using XDrishti.Application.Tests.Market.Bars;
using XDrishti.Application.Tests.Market.Calendar;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Market.Quality;
using static XDrishti.Application.Tests.Market.Quality.QualityBars;

namespace XDrishti.Application.Tests.Market.Quality;

/// <summary>The reads of the Data quality screen: overview, gate, runs, the instruments table and one instrument's detail.</summary>
public sealed class QualityQueryHandlersTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 6, 30, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly StubQualityQueries _queries = new();
    private readonly FakeDataSettings _settings = new();
    private readonly FixedCalendar _calendar = new();
    private readonly FakeQualityData _data = new();
    private readonly FakeBarStore _bars = new();
    private readonly InMemoryActionData _actions = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static QualityRunView Run(string status = "succeeded") =>
        new(Guid.NewGuid(), "daily", status, Now, Now.AddSeconds(8), 5, 214, 200, 10, 4, 93.5, null, null, null);

    [Fact]
    public async Task The_overview_gathers_the_last_run_the_counts_the_gate_the_window_and_the_limits()
    {
        _queries.Counts = new QualityCounts(214, 214, 200, 10, 4, 0, 93.5);
        _queries.LastFullRun = Run();
        _settings.QualitySpikeMultiple = 15;
        _settings.QualityMaxMissingPercent = 2;

        var overview = await new GetQualityOverviewHandler(_queries, _calendar, _settings, _actions, _clock).HandleAsync(new GetQualityOverviewQuery(), Ct);

        overview.LastRun.ShouldBe(_queries.LastFullRun);
        overview.Counts.ShouldBe(_queries.Counts);
        (overview.Gate.Ok, overview.Gate.Blocked, overview.Gate.FailedInstruments).ShouldBe((false, false, 4));
        overview.Window.ShouldBe(new QualityWindowView(5, Mon, Fri));
        (overview.SpikeMultiple, overview.MaxMissingPercent).ShouldBe((15.0, 2.0));
    }

    [Fact]
    public async Task Before_the_first_run_the_overview_has_no_run_and_a_blocked_gate()
    {
        var overview = await new GetQualityOverviewHandler(_queries, _calendar, _settings, _actions, _clock).HandleAsync(new GetQualityOverviewQuery(), Ct);

        (overview.LastRun, overview.Gate.Blocked).ShouldBe((null, true));
    }

    [Fact]
    public async Task The_gate_uses_the_limit_of_the_settings()
    {
        _queries.Counts = new QualityCounts(100, 100, 94, 0, 6, 0, 90);
        _queries.LastFullRun = Run();
        var handler = new GetQualityGateHandler(_queries, _calendar, _settings, _clock);

        var strict = await handler.HandleAsync(new GetQualityGateQuery(), Ct);
        _settings.QualityMaxFailedPercent = 10;
        var lenient = await handler.HandleAsync(new GetQualityGateQuery(), Ct);

        (strict.Blocked, lenient.Blocked, lenient.MaxFailedPercent).ShouldBe((true, false, 10.0));
    }

    [Fact]
    public async Task A_running_full_run_does_not_hide_a_failed_finished_one_and_with_no_finished_one_the_gate_stays_blocked()
    {
        _queries.Counts = new QualityCounts(100, 100, 100, 0, 0, 0, 90);
        _queries.LastFullRun = Run("running");
        var handler = new GetQualityGateHandler(_queries, _calendar, _settings, _clock);

        var none = await handler.HandleAsync(new GetQualityGateQuery(), Ct);
        _queries.PreviousFinishedRun = Run("failed");
        var failed = await handler.HandleAsync(new GetQualityGateQuery(), Ct);
        _queries.PreviousFinishedRun = Run();
        var ok = await handler.HandleAsync(new GetQualityGateQuery(), Ct);

        (none.Blocked, none.Reasons.Single()).ShouldBe((true, "There is no completed data quality run over every instrument yet."));
        (failed.Blocked, failed.Reasons.Single()).ShouldBe((true, "The last data quality run failed."));
        (ok.Blocked, ok.Ok).ShouldBe((false, true));
    }

    [Theory]
    [InlineData(null, 20)]
    [InlineData(0, 1)]
    [InlineData(500, 100)]
    [InlineData(7, 7)]
    public async Task The_history_takes_20_by_default_and_is_kept_within_1_to_100(int? take, int expected)
    {
        await new ListQualityRunsHandler(_queries).HandleAsync(new ListQualityRunsQuery(take), Ct);

        _queries.LastTake.ShouldBe(expected);
    }

    [Fact]
    public async Task The_table_search_is_checked_and_passed_on()
    {
        var handler = new ListQualityInstrumentsHandler(_queries);

        var result = await handler.HandleAsync(new ListQualityInstrumentsQuery(new QualityInstrumentRequest(" abb ", "NOTCHECKED", "-score", 2, 25)), Ct);

        result.IsSuccess.ShouldBeTrue();
        _queries.LastSearch.ShouldBe(new QualityInstrumentSearch("abb", "notChecked", QualitySortField.Score, true, 2, 25));
    }

    [Fact]
    public async Task Defaults_are_symbol_order_page_one_and_fifty_rows()
    {
        await new ListQualityInstrumentsHandler(_queries).HandleAsync(new ListQualityInstrumentsQuery(new QualityInstrumentRequest(null, null, null, null, null)), Ct);

        _queries.LastSearch.ShouldBe(new QualityInstrumentSearch(null, null, QualitySortField.Symbol, false, 1, 50));
    }

    [Theory]
    [InlineData("status", "bogus", "status")]
    [InlineData("sort", "bogus", "sort")]
    [InlineData("sort", "7", "sort")]
    [InlineData("page", "0", "page")]
    [InlineData("pageSize", "201", "pageSize")]
    [InlineData("q", "tab\there", "q")]
    public void A_bad_search_is_refused_with_the_field(string field, string value, string expected)
    {
        var request = field switch
        {
            "status" => new QualityInstrumentRequest(null, value, null, null, null),
            "sort" => new QualityInstrumentRequest(null, null, value, null, null),
            "page" => new QualityInstrumentRequest(null, null, null, int.Parse(value, System.Globalization.CultureInfo.InvariantCulture), null),
            "pageSize" => new QualityInstrumentRequest(null, null, null, null, int.Parse(value, System.Globalization.CultureInfo.InvariantCulture)),
            _ => new QualityInstrumentRequest(value, null, null, null, null),
        };

        var result = ListQualityInstrumentsHandler.Check(request);

        result.Error.Code.ShouldBe("data.invalid");
        result.Error.Fields!.Select(f => f.Field).ShouldBe([expected]);
    }

    [Fact]
    public void A_search_text_over_100_characters_is_refused()
    {
        ListQualityInstrumentsHandler.Check(new QualityInstrumentRequest(new string('a', 101), null, null, null, null)).IsFailure.ShouldBeTrue();
    }

    private GetQualityInstrumentHandler Detail => new(_queries, _data, _bars, _calendar, _settings, _clock);

    private static StoredQualityResult Stored(Guid id, QualityChecks? checks, string? status = "pass") =>
        new(id, "NSE:ABB", "ABB Ltd", "share", true, "dhan", Now.AddDays(-90), Now.AddDays(-3), checks is null ? null : Guid.NewGuid(), Now.AddHours(-1), status, checks is null ? null : 100, checks is null ? null : 1875, checks is null ? null : 0, checks);

    private Guid Seed(List<Bar>? bars = null, bool storedBuckets = true)
    {
        var id = Guid.CreateVersion7();
        bars ??= FullWindow();
        _bars.Stored[id] = bars;
        _data.Stored[id] = storedBuckets
            ? [.. MinuteAggregator.Build(bars).Select(b => new StoredBucket(b.Key.Timeframe, b.Key.Start, b.Value.Open, b.Value.High, b.Value.Low, b.Value.Close, b.Value.Volume, b.Value.Bars))]
            : [];
        return id;
    }

    [Fact]
    public async Task An_unknown_instrument_is_not_found()
    {
        var result = await Detail.HandleAsync(new GetQualityInstrumentQuery(Guid.NewGuid(), null, null), Ct);

        result.Error.Code.ShouldBe("data.instrument_not_found");
    }

    [Theory]
    [InlineData("1m")]
    [InlineData("3h")]
    [InlineData("1d")]
    public async Task A_timeframe_that_is_not_an_aggregate_is_refused(string timeframe)
    {
        var result = await Detail.HandleAsync(new GetQualityInstrumentQuery(Guid.NewGuid(), null, timeframe), Ct);

        result.Error.Fields!.Single().Field.ShouldBe("timeframe");
    }

    [Fact]
    public async Task An_instrument_never_checked_is_not_checked_but_shows_its_buckets_as_they_are_now()
    {
        var id = Seed();
        _queries.Found = Stored(id, null, null);

        var detail = (await Detail.HandleAsync(new GetQualityInstrumentQuery(id, null, null), Ct)).Value;

        (detail.Status, detail.Checks, detail.Score, detail.RunId).ShouldBe(("notChecked", null, null, null));
        detail.Missing.ShouldBe(new QualityMissing(0, false, []));
        (detail.Buckets.Differences, detail.Buckets.Total).ShouldBeEquivalentTo((0, 5 * (75 + 38 + 25 + 13 + 7 + 1) + 2));
        detail.Buckets.ComputedAt.ShouldBe(Now);
    }

    [Fact]
    public async Task The_detail_gives_every_check_of_the_last_run()
    {
        var id = Seed();
        var checks = new QualityChecks(
            new CompletenessCheck(CheckStatus.Pass, 1875, 1875, 0, 0, 0, []),
            new SanityCheck(CheckStatus.Pass, 1875, 0, []),
            new SpikeCheck(CheckStatus.Pass, 0, 0, 0.01, 1, []),
            new AggregatesCheck(CheckStatus.Pass, 10, 0, new Dictionary<string, TimeframeCheck>()),
            new FreshnessCheck(CheckStatus.Pass, null, Fri, 0),
            OfficialDailyCheck.NotAvailable,
            new QualityDetails([], []));
        _queries.Found = Stored(id, checks);

        var detail = (await Detail.HandleAsync(new GetQualityInstrumentQuery(id, null, null), Ct)).Value;

        detail.Checks!.Completeness.ShouldBe(checks.Completeness);
        detail.Checks.Sanity.ShouldBe(checks.Sanity);
        detail.Checks.Spikes.ShouldBe(checks.Spikes);
        detail.Checks.Freshness.ShouldBe(checks.Freshness);
        detail.Checks.OfficialDaily.ShouldBe(checks.OfficialDaily);
        (detail.Checks.Aggregates.Status, detail.Checks.Aggregates.Compared).ShouldBe((CheckStatus.Pass, 10));
        (detail.Symbol, detail.Status, detail.Score, detail.BarsChecked, detail.Source, detail.Tracked).ShouldBe(("NSE:ABB", "pass", 100, 1875, "dhan", true));
        detail.CheckedAt!.Value.Offset.ShouldBe(TimeSpan.FromMinutes(330));
    }

    [Fact]
    public async Task Missing_minutes_are_expanded_from_the_ranges_and_the_total_stays_exact()
    {
        var id = Seed();
        var checks = new QualityChecks(
            new CompletenessCheck(CheckStatus.Warning, 1875, 1872, 3, 0.16, 0, []),
            new SanityCheck(CheckStatus.Pass, 1872, 0, []),
            new SpikeCheck(CheckStatus.Pass, 0, 0, 0, 1, []),
            new AggregatesCheck(CheckStatus.Pass, 10, 0, new Dictionary<string, TimeframeCheck>()),
            new FreshnessCheck(CheckStatus.Pass, null, Fri, 0),
            OfficialDailyCheck.NotAvailable,
            new QualityDetails([new MissingRange(Wed, 9 * 60 + 58, 2), new MissingRange(Thu, 15 * 60 + 29, 1)], []));
        _queries.Found = Stored(id, checks, "warning");

        var missing = (await Detail.HandleAsync(new GetQualityInstrumentQuery(id, null, null), Ct)).Value.Missing;

        missing.Total.ShouldBe(3);
        missing.Truncated.ShouldBeFalse();
        missing.Items.ShouldBe([new MissingMinute(Wed, "09:58"), new MissingMinute(Wed, "09:59"), new MissingMinute(Thu, "15:29")]);
    }

    [Fact]
    public void The_missing_minutes_listed_are_capped_at_500_with_the_total_given()
    {
        var checks = new QualityChecks(
            new CompletenessCheck(CheckStatus.Warning, 1875, 1000, 875, 46.67, 0, []),
            new SanityCheck(CheckStatus.Pass, 0, 0, []),
            new SpikeCheck(CheckStatus.Pass, 0, 0, 0, 1, []),
            new AggregatesCheck(CheckStatus.Pass, 0, 0, new Dictionary<string, TimeframeCheck>()),
            new FreshnessCheck(CheckStatus.Pass, null, Fri, 0),
            OfficialDailyCheck.NotAvailable,
            new QualityDetails([new MissingRange(Mon, 555, 375), new MissingRange(Tue, 555, 375), new MissingRange(Wed, 555, 125)], []));

        var missing = GetQualityInstrumentHandler.Missing(checks);

        (missing.Total, missing.Truncated, missing.Items.Count).ShouldBe((875, true, 500));
        missing.Items[375].ShouldBe(new MissingMinute(Tue, "09:15"));
    }

    [Fact]
    public async Task Buckets_tampered_since_the_run_show_as_differences_and_differences_only_leaves_the_rest_out()
    {
        var id = Seed();
        var stored = _data.Stored[id].ToList();
        var bucket = stored.First(b => b.Timeframe == Timeframe.M30 && b.Start == Epoch(Wed, 10 * 60 + 15));
        stored[stored.IndexOf(bucket)] = bucket with { Low = bucket.Low - 1 };
        _data.Stored[id] = stored;
        _queries.Found = Stored(id, null, null);

        var all = (await Detail.HandleAsync(new GetQualityInstrumentQuery(id, false, null), Ct)).Value.Buckets;
        var differing = (await Detail.HandleAsync(new GetQualityInstrumentQuery(id, true, null), Ct)).Value.Buckets;

        all.Differences.ShouldBe(1);
        all.Items.Count.ShouldBe(all.Total);
        differing.Items.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            row => row.Timeframe.ShouldBe("30m"),
            row => row.Fields.ShouldBe(["low"]),
            row => row.Stored!.Low.ShouldBe(bucket.Low - 1),
            row => row.Recomputed!.Low.ShouldBe(bucket.Low),
            row => row.Ts.ShouldBe(new DateTimeOffset(2026, 10, 7, 10, 15, 0, TimeSpan.FromMinutes(330))));
        differing.Total.ShouldBe(all.Total);
    }

    [Fact]
    public async Task The_timeframe_filter_keeps_one_timeframe_and_counts_its_buckets()
    {
        var id = Seed();
        _queries.Found = Stored(id, null, null);

        var buckets = (await Detail.HandleAsync(new GetQualityInstrumentQuery(id, null, "1D"), Ct)).Value.Buckets;

        buckets.Total.ShouldBe(5);
        buckets.Items.ShouldAllBe(r => r.Timeframe == "1D");
    }

    [Fact]
    public async Task Buckets_are_cut_at_2000_rows_and_say_so()
    {
        var id = Seed(storedBuckets: false); // nothing stored: every bucket differs
        _queries.Found = Stored(id, null, null);
        _settings.QualitySessions = 60;
        _bars.Stored[id] = Enumerable.Range(0, 60).Select(i => new DateOnly(2026, 7, 13).AddDays(i)).Where(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).SelectMany(d => Session(d)).ToList();

        var buckets = (await Detail.HandleAsync(new GetQualityInstrumentQuery(id, null, null), Ct)).Value.Buckets;

        buckets.Total.ShouldBeGreaterThan(2000);
        (buckets.Items.Count, buckets.Truncated).ShouldBe((2000, true));
    }

    [Fact]
    public async Task An_instrument_without_bars_has_no_buckets()
    {
        var id = Guid.NewGuid();
        _queries.Found = new StoredQualityResult(id, "NSE:NEW", "New Ltd", "share", true, null, null, null, null, null, null, null, null, null, null);

        var detail = (await Detail.HandleAsync(new GetQualityInstrumentQuery(id, null, null), Ct)).Value;

        detail.Buckets.ShouldBe(new QualityBuckets(Now, 0, 0, false, []));
        (detail.FirstBar, detail.LastBar).ShouldBe((null, null));
    }

    [Fact]
    public async Task A_calendar_holiday_moves_the_window_the_buckets_are_compared_over()
    {
        _calendar.Calendar = new TradingCalendar([TradingDay.Holiday(Wed, CalendarSource.Manual, CalendarStatus.Confirmed, null, Now)]);
        var id = Seed(Sessions(new DateOnly(2026, 10, 2), Mon, Tue, Thu, Fri));
        _queries.Found = Stored(id, null, null);

        var buckets = (await Detail.HandleAsync(new GetQualityInstrumentQuery(id, null, "1D"), Ct)).Value.Buckets;

        buckets.Items.Select(r => r.Ts.Day).ShouldBe([2, 5, 6, 8, 9]);
    }
}
