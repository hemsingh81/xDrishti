using System.Net;
using System.Net.Http.Json;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Market.Bars;
using XDrishti.Application.Market.Bars.Eod;
using XDrishti.Application.Market.Bars.Gaps;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Api.IntegrationTests.Data;

/// <summary>
/// Gap detection and backfill inside the Dhan phase on real TimescaleDB (ADR 0017 §3, P1-12a): ten sessions from CSV with one partial day, a fake Dhan source behind the
/// port, the same merge, dirty ranges and aggregates as every fetch. The clock is Monday 2026-10-12 12:00 India time, so the last closed session is Friday 2026-10-09.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class DataGapBackfillTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly DateOnly Partial = new(2026, 10, 6);

    private static readonly DateOnly[] Sessions =
        [new(2026, 9, 28), new(2026, 9, 29), new(2026, 9, 30), new(2026, 10, 1), new(2026, 10, 2), new(2026, 10, 5), Partial, new(2026, 10, 7), new(2026, 10, 8), new(2026, 10, 9)];

    private static readonly string[] DhanAndAggregate = ["dhan", "aggregate"];

    private const int PartialBars = 200;

    private static readonly int FirstMissing = SessionClock.OpenMinute + PartialBars;

    private static List<Bar> History(int seed) =>
        [.. Sessions.SelectMany(d => BarFixtures.Session(d, seed, count: d == Partial ? PartialBars : SessionClock.RegularBars))];

    /// <summary>The bars of the partial day that the CSV file lacks.</summary>
    private static List<Bar> Missing(int seed, int count = SessionClock.RegularBars - PartialBars) => BarFixtures.Session(Partial, seed, startMinute: FirstMissing, count: count);

    private async Task<Guid> LoadAsync(string ticker, int seed)
    {
        var id = await SeedAsync(ticker);
        using (var tracked = await Client.PostJsonAsync("/api/instruments/tracked", new { instrumentIds = new[] { id } }, Ct))
        {
            tracked.EnsureSuccessStatusCode();
        }

        WriteCsv(ticker, History(seed));
        var job = await RunAsync(["csv", "aggregate"]);
        job.Status.ShouldBe("succeeded", job.Error);
        return id;
    }

    private async Task<DataJobView> DhanAsync(bool refetchGaps = false)
    {
        using var response = await Client.PostJsonAsync("/api/data/refresh", new { steps = DhanAndAggregate, refetchGaps }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
        var started = (await response.Content.ReadFromJsonAsync<DataJobView>(Ct))!;
        await Factory.DataJobs.RunPendingAsync(Factory.Services, Ct);
        var job = await GetJobAsync(started.Id);
        job.Status.ShouldNotBe("running");
        return job;
    }

    private async Task<GapPage> GapsAsync(string query = "")
    {
        using var response = await Client.GetAsync("/api/data/gaps" + query, Ct);
        return await response.ReadAsync<GapPage>(Ct);
    }

    private async Task<DataNotices> NoticesAsync()
    {
        using var response = await Client.GetAsync("/api/data/notices", Ct);
        return await response.ReadAsync<DataNotices>(Ct);
    }

    private static bool IsPartialDayWindow(FetchWindow w) => w.From.ToUnixTimeSeconds() == BarFixtures.Epoch(Partial, 0) && w.To.ToUnixTimeSeconds() == BarFixtures.Epoch(Partial.AddDays(1), 0);

    [Fact]
    public async Task A_partial_day_is_found_refetched_in_its_own_window_merged_with_dhan_precedence_and_aggregated_again()
    {
        var abb = await LoadAsync("ABB", 1);
        var source = Factory.Dhan.UseSource(t => new IntradayResult(t.InstrumentId, [.. t.Windows.Any(IsPartialDayWindow) ? Missing(1) : []], t.Windows.Count));
        Factory.Dhan.UseConnectedAccount();
        (await ScalarAsync<int>("SELECT bars FROM market.bars_agg WHERE instrument_id = @id AND timeframe = '1D' AND ts = to_timestamp(@ts)", ("id", abb), ("ts", (double)BarFixtures.Epoch(Partial, 0)))).ShouldBe(PartialBars);

        var job = await DhanAsync();

        job.Status.ShouldBe("succeeded", job.Error);
        var windows = source.Plans.ShouldHaveSingleItem().Targets.ShouldHaveSingleItem().Windows;
        windows.Count.ShouldBe(2);
        IsPartialDayWindow(windows[0]).ShouldBeTrue();
        (job.Dhan.GapsFound, job.Dhan.GapsFilled, job.Dhan.GapsUnfillable).ShouldBe((1, 1, 0));
        (job.Dhan.Inserted, job.Dhan.Status).ShouldBe((SessionClock.RegularBars - PartialBars, "succeeded"));
        var gap = (await GapsAsync()).Items.ShouldHaveSingleItem();
        (gap.Symbol, gap.Name, gap.Date, gap.Expected, gap.Found, gap.Attempts, gap.Status).ShouldBe(("NSE:ABB", "ABB Ltd", Partial, 375, 375, 1, "filled"));
        gap.LastAttemptAt.ShouldNotBeNull();
        (await ScalarAsync<long>("SELECT count(*) FROM market.bars_1m WHERE instrument_id = @id AND source = 'dhan'", ("id", abb))).ShouldBe(SessionClock.RegularBars - PartialBars);
        var day = await ScalarAsync<int>("SELECT bars FROM market.bars_agg WHERE instrument_id = @id AND timeframe = '1D' AND ts = to_timestamp(@ts)", ("id", abb), ("ts", (double)BarFixtures.Epoch(Partial, 0)));
        day.ShouldBe(SessionClock.RegularBars); // the aggregates were rebuilt from the dirty range
        (await ScalarAsync<bool>("SELECT complete FROM market.bars_agg WHERE instrument_id = @id AND timeframe = '1D' AND ts = to_timestamp(@ts)", ("id", abb), ("ts", (double)BarFixtures.Epoch(Partial, 0)))).ShouldBeTrue();
        var overview = await OverviewAsync();
        (overview.Gaps.Open, overview.Gaps.Unfillable, overview.Gaps.Filled).ShouldBe((0, 0, 1));
        overview.Gaps.LastDetectedAt.ShouldNotBeNull();
        (await NoticesAsync()).Items.ShouldBeEmpty();
        (await InstrumentsAsync()).Items.Single().OpenGaps.ShouldBe(0);
    }

    [Fact]
    public async Task A_refetch_that_adds_nothing_is_unfillable_a_scheduled_run_leaves_it_alone_and_re_fetching_gaps_tries_again()
    {
        var abb = await LoadAsync("ABB", 1);
        var serve = false;
        var source = Factory.Dhan.UseSource(t => new IntradayResult(t.InstrumentId, [.. serve && t.Windows.Any(IsPartialDayWindow) ? Missing(1) : []], t.Windows.Count));
        Factory.Dhan.UseConnectedAccount();

        var first = await DhanAsync();
        var second = await DhanAsync();
        serve = true;
        var third = await DhanAsync(refetchGaps: true);

        source.Plans.Count.ShouldBe(3);
        source.Plans[0].Targets.Single().Windows.Count.ShouldBe(2);
        source.Plans[1].Targets.Single().Windows.Count.ShouldBe(1); // unfillable: not asked for by a scheduled or ordinary run
        source.Plans[2].Targets.Single().Windows.Count.ShouldBe(2);
        (first.Dhan.GapsFound, first.Dhan.GapsUnfillable).ShouldBe((1, 1));
        (second.Dhan.GapsFound, second.Dhan.GapsUnfillable, second.Dhan.GapsFilled).ShouldBe((0, 0, 0));
        (third.Dhan.GapsFound, third.Dhan.GapsFilled, third.RefetchGaps).ShouldBe((1, 1, true));
        var gap = (await GapsAsync()).Items.Single();
        (gap.Status, gap.Attempts, gap.Found).ShouldBe(("filled", 2, 375));
        (await StoredBarsAsync(abb)).Count.ShouldBe(Sessions.Length * SessionClock.RegularBars);
    }

    [Fact]
    public async Task A_refetch_that_adds_some_bars_leaves_the_gap_open_and_shows_up_in_the_lists_the_filter_the_overview_and_the_notices()
    {
        var abb = await LoadAsync("ABB", 1);
        await LoadAsync("TCS", 2);
        Factory.Dhan.UseSource(t => new IntradayResult(t.InstrumentId, [.. t.Symbol == "NSE:ABB" && t.Windows.Any(IsPartialDayWindow) ? Missing(1, count: 100) : []], t.Windows.Count));
        Factory.Dhan.UseConnectedAccount();

        var job = await DhanAsync();

        (job.Dhan.GapsFound, job.Dhan.GapsFilled, job.Dhan.GapsUnfillable).ShouldBe((2, 0, 1)); // ABB gained 100 of 175 bars, TCS gained none
        job.Warnings.ShouldHaveSingleItem().ShouldBe("1 sessions still have missing bars after the fetch; the next update asks for them again.");
        var open = await GapsAsync("?status=open");
        var row = open.Items.ShouldHaveSingleItem();
        (row.InstrumentId, row.Symbol, row.Found, row.Attempts, row.Status).ShouldBe((abb, "NSE:ABB", PartialBars + 100, 1, "open"));
        (await GapsAsync("?status=unfillable")).Items.Single().Symbol.ShouldBe("NSE:TCS");
        (await GapsAsync()).Total.ShouldBe(2);
        (await GapsAsync($"?instrumentId={abb}")).Items.ShouldHaveSingleItem().Symbol.ShouldBe("NSE:ABB");
        (await GapsAsync("?status=filled")).Items.ShouldBeEmpty();
        (await GapsAsync("?pageSize=1&page=2")).Items.ShouldHaveSingleItem();
        var overview = await OverviewAsync();
        (overview.Gaps.Open, overview.Gaps.Unfillable, overview.Gaps.Filled).ShouldBe((1, 1, 0));
        var withGaps = await InstrumentsAsync("?hasGaps=true");
        withGaps.Items.ShouldHaveSingleItem().Symbol.ShouldBe("NSE:ABB");
        withGaps.Items[0].OpenGaps.ShouldBe(1);
        (await InstrumentsAsync()).Items.Select(i => (i.Symbol, i.OpenGaps)).ShouldBe([("NSE:ABB", 1), ("NSE:TCS", 0)]);
        var notice = (await NoticesAsync()).Items.ShouldHaveSingleItem();
        (notice.Kind, notice.Severity, notice.Text).ShouldBe(("data_gaps_open", "warn", "1 trading session has missing price bars"));
    }

    [Fact]
    public async Task The_gaps_of_an_untracked_or_retired_instrument_are_not_counted_anywhere_and_come_back_when_it_is_tracked_again()
    {
        var abb = await LoadAsync("ABB", 1);
        Factory.Dhan.UseSource(t => new IntradayResult(t.InstrumentId, [.. t.Windows.Any(IsPartialDayWindow) ? Missing(1, count: 100) : []], t.Windows.Count));
        Factory.Dhan.UseConnectedAccount();
        await DhanAsync();
        (await OverviewAsync()).Gaps.Open.ShouldBe(1);

        await SetTrackingAsync(abb, active: false, retired: false);
        await AssertNoGapsShownAsync(abb);
        await SetTrackingAsync(abb, active: true, retired: true);
        await AssertNoGapsShownAsync(abb);
        await SetTrackingAsync(abb, active: true, retired: false);

        (await OverviewAsync()).Gaps.Open.ShouldBe(1);
        (await GapsAsync()).Items.ShouldHaveSingleItem().Symbol.ShouldBe("NSE:ABB");
        (await InstrumentsAsync("?hasGaps=true")).Items.ShouldHaveSingleItem().OpenGaps.ShouldBe(1);
        (await NoticesAsync()).Items.ShouldHaveSingleItem().Kind.ShouldBe("data_gaps_open");
        (await ScalarAsync<long>("SELECT count(*) FROM market.bar_gaps WHERE instrument_id = @id", ("id", abb))).ShouldBe(1); // stored all along
    }

    private async Task SetTrackingAsync(Guid id, bool active, bool retired)
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "UPDATE market.tracked_instruments SET active = @active, deactivated_at = CASE WHEN @active THEN NULL ELSE now() END WHERE instrument_id = @id", ("id", id), ("active", active));
        await ExecuteAsync(connection, "UPDATE market.instruments SET retired_at = CASE WHEN @retired THEN now() END WHERE id = @id", ("id", id), ("retired", retired));
    }

    private async Task AssertNoGapsShownAsync(Guid id)
    {
        var overview = await OverviewAsync();
        (overview.Gaps.Open, overview.Gaps.Unfillable, overview.Gaps.Filled).ShouldBe((0, 0, 0));
        (await GapsAsync()).Total.ShouldBe(0);
        (await GapsAsync($"?instrumentId={id}")).Items.ShouldBeEmpty();
        (await InstrumentsAsync("?hasGaps=true")).Items.ShouldBeEmpty();
        (await NoticesAsync()).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_instrument_that_could_not_be_fetched_keeps_its_gap_open_for_the_next_run()
    {
        await LoadAsync("ABB", 1);
        Factory.Dhan.UseSource(t => new IntradayResult(t.InstrumentId, [], 1, new IntradayFailure(IntradayFailureKind.Failed, "timeout")));
        Factory.Dhan.UseConnectedAccount();

        var job = await DhanAsync();

        job.Dhan.Status.ShouldBe("failed");
        var gap = (await GapsAsync()).Items.ShouldHaveSingleItem();
        (gap.Status, gap.Attempts, gap.LastAttemptAt).ShouldBe(("open", 0, null));
    }

    [Fact]
    public async Task A_holiday_and_a_weekend_are_not_gaps_even_though_they_have_no_bars_and_a_short_special_session_is_complete_with_its_own_length()
    {
        var holiday = new DateOnly(2026, 10, 7);
        var saturday = new DateOnly(2026, 10, 3);
        var id = await SeedAsync("ABB");
        using (var tracked = await Client.PostJsonAsync("/api/instruments/tracked", new { instrumentIds = new[] { id } }, Ct))
        {
            tracked.EnsureSuccessStatusCode();
        }

        var bars = Sessions.Where(d => d != holiday).SelectMany(d => BarFixtures.Session(d, 1)).ToList();
        bars.AddRange(BarFixtures.Session(saturday, 1, count: 60)); // 09:15-10:15 special session
        WriteCsv("ABB", [.. bars.OrderBy(b => b.EpochSeconds)]);
        (await Client.PostJsonAsync("/api/data/calendar", new { date = "2026-10-07", kind = "holiday" }, Ct)).EnsureSuccessStatusCode();
        (await Client.PostJsonAsync("/api/data/calendar", new { date = "2026-10-03", kind = "special", sessionFrom = "09:15", sessionTo = "10:15" }, Ct)).EnsureSuccessStatusCode();
        (await RunAsync(["csv", "aggregate"])).Status.ShouldBe("succeeded");
        Factory.Dhan.UseSource(t => new IntradayResult(t.InstrumentId, [], 1));
        Factory.Dhan.UseConnectedAccount();

        var job = await DhanAsync();

        job.Status.ShouldBe("succeeded", job.Error);
        (job.Dhan.GapsFound, (await GapsAsync()).Total).ShouldBe((0, 0));
    }

    [Fact]
    public async Task A_special_session_shorter_than_its_expected_length_is_a_gap_of_that_length()
    {
        var saturday = new DateOnly(2026, 10, 3);
        var id = await SeedAsync("ABB");
        using (var tracked = await Client.PostJsonAsync("/api/instruments/tracked", new { instrumentIds = new[] { id } }, Ct))
        {
            tracked.EnsureSuccessStatusCode();
        }

        var bars = Sessions.Where(d => d != Partial).SelectMany(d => BarFixtures.Session(d, 1)).Concat(BarFixtures.Session(saturday, 1, count: 40)).ToList();
        bars.AddRange(BarFixtures.Session(Partial, 1));
        WriteCsv("ABB", [.. bars.OrderBy(b => b.EpochSeconds)]);
        (await Client.PostJsonAsync("/api/data/calendar", new { date = "2026-10-03", kind = "special", sessionFrom = "09:15", sessionTo = "10:15" }, Ct)).EnsureSuccessStatusCode();
        (await RunAsync(["csv", "aggregate"])).Status.ShouldBe("succeeded");
        Factory.Dhan.UseSource(t => new IntradayResult(t.InstrumentId, [], 1));
        Factory.Dhan.UseConnectedAccount();

        await DhanAsync();

        var gap = (await GapsAsync()).Items.ShouldHaveSingleItem();
        (gap.Date, gap.Expected, gap.Found, gap.Status).ShouldBe((saturday, 60, 40, "unfillable"));
    }

    [Fact]
    public async Task The_lookback_limits_how_far_back_sessions_are_examined()
    {
        // Data:Gaps:LookbackSessions is 30: a partial day 40 sessions ago is not looked for.
        var id = await SeedAsync("ABB");
        using (var tracked = await Client.PostJsonAsync("/api/instruments/tracked", new { instrumentIds = new[] { id } }, Ct))
        {
            tracked.EnsureSuccessStatusCode();
        }

        var days = Enumerable.Range(0, 60).Select(i => LastSession.AddDays(-i)).Where(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).OrderBy(d => d).ToList();
        var old = days[days.Count - 41];
        var recent = days[days.Count - 5];
        WriteCsv("ABB", [.. days.SelectMany(d => BarFixtures.Session(d, 1, count: d == old || d == recent ? 100 : SessionClock.RegularBars))]);
        (await RunAsync(["csv", "aggregate"])).Status.ShouldBe("succeeded");
        Factory.Dhan.UseSource(t => new IntradayResult(t.InstrumentId, [], 1));
        Factory.Dhan.UseConnectedAccount();

        await DhanAsync();

        (await GapsAsync()).Items.Select(g => g.Date).ShouldBe([recent]);
    }

    [Fact]
    public async Task A_validation_problem_names_the_field()
    {
        using var response = await Client.GetAsync("/api/data/gaps?status=closed&pageSize=0", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync(Ct);
        body.ShouldContain("data.invalid");
        body.ShouldContain("status");
        body.ShouldContain("pageSize");
    }
}
