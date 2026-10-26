using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Application.Market.Bars.Eod;
using XDrishti.Application.Market.Context;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Context;

namespace XDrishti.Api.IntegrationTests.Data;

/// <summary>
/// P3-01d end to end on the real database: the data job writes the market context from the stored daily rows of the two indices as its last step, the table is insert-only, the coverage endpoint and the notices tell the truth, and the reader returns the figures
/// of the latest stored day before a date. The clock is pinned to Monday 2026-10-12 12:00 India time; the last closed session is Friday 2026-10-09.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class DataContextTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly DateOnly[] Sessions = [new(2026, 10, 7), new(2026, 10, 8), LastSession];

    /// <summary>
    /// Seeds NIFTY and INDIAVIX as tracked indices with a Dhan id in IDX_I and makes the fake Dhan source answer with their 1-minute bars (the CSV loader reads shares and ETFs only): <paramref name="niftyBars"/> bars on each day
    /// of <paramref name="niftyDays"/> and a full session on each day of <paramref name="vixDays"/>. A job with the steps dhan and aggregate then does the whole path.
    /// </summary>
    private async Task LoadIndicesAsync(DateOnly[]? niftyDays = null, DateOnly[]? vixDays = null, int niftyBars = 375, bool trackVix = true)
    {
        var nifty = await SeedAsync("NIFTY", type: "index", dhanId: false);
        var vix = await SeedAsync("INDIAVIX", type: "index", dhanId: false);
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "INSERT INTO market.instrument_broker_ids (instrument_id, broker, broker_id, segment) VALUES (@n, 'dhan', '13', 'IDX_I'), (@v, 'dhan', '21', 'IDX_I')", ("n", nifty), ("v", vix));
        }

        await TrackAsync(trackVix ? [nifty, vix] : [nifty]);
        var bars = new Dictionary<Guid, List<Bar>>
        {
            [nifty] = [.. (niftyDays ?? Sessions).SelectMany(d => BarFixtures.Session(d, 1, count: niftyBars))],
            [vix] = [.. (vixDays ?? Sessions).SelectMany(d => BarFixtures.Session(d, 2))],
        };
        Factory.Dhan.UseSource(t => new IntradayResult(t.InstrumentId, bars[t.InstrumentId], t.Windows.Count));
        Factory.Dhan.UseConnectedAccount();
    }

    private async Task<ContextCoverageView> CoverageAsync()
    {
        using var response = await Client.GetAsync("/api/data/context", Ct);
        return await response.ReadAsync<ContextCoverageView>(Ct);
    }

    private async Task<DataNotices> NoticesAsync()
    {
        using var response = await Client.GetAsync("/api/data/notices", Ct);
        return await response.ReadAsync<DataNotices>(Ct);
    }

    private async Task InsertContextRowAsync(DateOnly date, double close = 22_000, double? vix = 15)
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(
            connection,
            "INSERT INTO market.context_daily (date, index_instrument_id, index_high, index_low, index_close, vix_close, index_bars, vix_bars, source, written_at) VALUES (@d, gen_random_uuid(), @h, @l, @c, @v, 375, 375, 'derived_1m', now())",
            ("d", date), ("h", close + 10), ("l", close - 10), ("c", close), ("v", (object?)vix ?? DBNull.Value));
    }

    [Fact]
    public async Task A_data_job_that_aggregates_writes_the_context_days_as_its_last_step_and_the_coverage_says_so()
    {
        await LoadIndicesAsync();

        var job = await RunAsync(["dhan", "aggregate"]);

        job.Status.ShouldBe("succeeded");
        job.Warnings.ShouldBeEmpty();
        var coverage = await CoverageAsync();
        (coverage.Days, coverage.FirstDate, coverage.LastDate, coverage.VixDays, coverage.VixMissingDays).ShouldBe((3, Sessions[0], LastSession, 3, 0));
        (coverage.Missing, coverage.IndexSymbol, coverage.VixSymbol, coverage.AgeSessions, coverage.Stale).ShouldBe((null, "NSE:NIFTY", "NSE:INDIAVIX", 0, false));
        coverage.IndexTrendReadableFrom.ShouldBeNull();
        coverage.Source.ShouldBe("derived from 1-minute bars; the official daily close can differ slightly");
        (await ScalarAsync<long>("SELECT count(*) FROM market.context_daily WHERE source = 'derived_1m' AND index_bars = 375 AND vix_bars = 375")).ShouldBe(3);
        (await ScalarAsync<long>("SELECT count(*) FROM market.context_daily c JOIN market.instruments i ON i.id = c.index_instrument_id WHERE i.canonical_symbol = 'NSE:NIFTY'")).ShouldBe(3);
    }

    [Fact]
    public async Task A_second_run_leaves_the_stored_rows_untouched()
    {
        await LoadIndicesAsync();
        await RunAsync(["dhan", "aggregate"]);
        var before = await ScalarAsync<string>("SELECT string_agg(date::text || '|' || index_close::text || '|' || written_at::text, ',' ORDER BY date) FROM market.context_daily");
        Factory.Clock.Pin(Now.AddHours(2));
        await ResignInAsync();

        await RunAsync(["dhan", "aggregate"]);

        (await ScalarAsync<string>("SELECT string_agg(date::text || '|' || index_close::text || '|' || written_at::text, ',' ORDER BY date) FROM market.context_daily")).ShouldBe(before);
    }

    [Fact]
    public async Task Writing_a_stored_date_again_does_nothing_and_the_old_values_stay()
    {
        await InsertContextRowAsync(LastSession, close: 22_000);
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IContextDayStore>();

        var inserted = await store.InsertAsync(Guid.NewGuid(), [new ContextDayRecord(LastSession, 99_999, 99_000, 99_500, 50, 375, 375), new ContextDayRecord(LastSession.AddDays(3), 22_100, 21_900, 22_000, null, 375, null)], Ct);

        inserted.ShouldBe(1);
        (await ScalarAsync<double>("SELECT index_close FROM market.context_daily WHERE date = @d", ("d", LastSession))).ShouldBe(22_000);
        (await ScalarAsync<long>("SELECT count(*) FROM market.context_daily")).ShouldBe(2);
    }

    [Fact]
    public async Task A_thin_index_day_is_skipped_with_its_bars_and_the_job_does_not_fail()
    {
        await LoadIndicesAsync(niftyBars: 300);

        var job = await RunAsync(["dhan", "aggregate"]);

        job.Status.ShouldBe("succeeded");
        var coverage = await CoverageAsync();
        coverage.Days.ShouldBe(0);
        coverage.SkippedTotal.ShouldBe(3);
        coverage.Skipped.ShouldAllBe(s => s.Reason == "index_thin" && s.Bars == 300);
    }

    [Fact]
    public async Task An_untracked_index_is_named_with_its_fix_nothing_is_written_and_the_job_succeeds()
    {
        var job = await RunAsync(["aggregate"]);

        job.Status.ShouldBe("succeeded");
        var coverage = await CoverageAsync();
        (coverage.Missing, coverage.Days).ShouldBe(("index_not_tracked", 0));
        coverage.Fix!.ShouldContain("Instruments screen");
        (await ScalarAsync<long>("SELECT count(*) FROM market.context_daily")).ShouldBe(0);
    }

    [Fact]
    public async Task An_untracked_vix_is_named_and_the_index_alone_writes_nothing()
    {
        await LoadIndicesAsync(trackVix: false);

        var job = await RunAsync(["dhan", "aggregate"]);

        job.Status.ShouldBe("succeeded");
        var coverage = await CoverageAsync();
        (coverage.Missing, coverage.Days).ShouldBe(("vix_not_tracked", 0));
        coverage.Fix!.ShouldContain("INDIA VIX");
    }

    [Fact]
    public async Task Stocks_do_not_become_the_context_and_an_index_is_found_by_its_display_name_when_the_symbol_is_unknown()
    {
        await SeedAsync("ABB");
        var id = await SeedAsync("NIFTY50", type: "index", dhanId: false, canonical: "NSE:NIFTY_BANK_X");
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "UPDATE market.instruments SET display_name = 'Nifty 50' WHERE id = @id", ("id", id));
            await ExecuteAsync(connection, "INSERT INTO market.instrument_broker_ids (instrument_id, broker, broker_id, segment) VALUES (@id, 'dhan', '13', 'IDX_I')", ("id", id));
        }

        var coverage = await CoverageAsync();

        coverage.Missing.ShouldBe("index_not_tracked", "an untracked instrument is not used even when its name matches");
    }

    [Fact]
    public async Task A_context_two_sessions_or_more_behind_raises_the_stale_notice_and_a_current_one_raises_nothing()
    {
        await InsertContextRowAsync(new DateOnly(2026, 10, 6));

        var stale = await NoticesAsync();
        var coverage = await CoverageAsync();
        await InsertContextRowAsync(new DateOnly(2026, 10, 7));
        await InsertContextRowAsync(new DateOnly(2026, 10, 8));
        await InsertContextRowAsync(LastSession);
        var current = await NoticesAsync();

        stale.Items.Select(n => n.Kind).ShouldBe(["data_context_stale"]);
        (coverage.AgeSessions, coverage.Stale).ShouldBe((3, true));
        current.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_session_missing_inside_the_stored_range_raises_the_gap_notice()
    {
        await InsertContextRowAsync(new DateOnly(2026, 10, 7));
        await InsertContextRowAsync(LastSession);

        var notices = await NoticesAsync();

        notices.Items.ShouldHaveSingleItem().Kind.ShouldBe("data_context_gap");
        (await CoverageAsync()).GapSessions.ShouldBe(1);
    }

    [Fact]
    public async Task The_reader_returns_the_figures_of_the_latest_day_strictly_before_the_date_and_names_why_it_cannot()
    {
        await InsertContextRowAsync(Sessions[0], 22_000);
        await InsertContextRowAsync(Sessions[1], 22_100);
        await InsertContextRowAsync(LastSession, 22_200, vix: null);
        using var scope = Factory.Services.CreateScope();
        var figures = scope.ServiceProvider.GetRequiredService<IContextFigures>();

        var monday = await figures.AsOfAsync(new DateOnly(2026, 10, 12), Ct);
        var onTheDay = await figures.AsOfAsync(LastSession, Ct);
        var first = await figures.AsOfAsync(Sessions[0], Ct);
        var far = await figures.AsOfAsync(LastSession.AddDays(8), Ct);

        monday.Figures!.AsOf.ShouldBe(LastSession);
        (monday.Figures.Basis, monday.Figures.Vix, monday.Figures.Regime, monday.Figures.Breadth).ShouldBe(("index_only", null, null, null));
        onTheDay.Figures!.AsOf.ShouldBe(Sessions[1], "a record dated D is never read for D");
        (first.Figures, first.Unavailable).ShouldBe((null, "before_history"));
        (far.Figures, far.Unavailable).ShouldBe((null, "stale"));
    }

    [Fact]
    public async Task The_reader_says_there_is_no_context_when_nothing_is_stored()
    {
        using var scope = Factory.Services.CreateScope();

        var read = await scope.ServiceProvider.GetRequiredService<IContextFigures>().AsOfAsync(LastSession, Ct);

        (read.Figures, read.Unavailable).ShouldBe((null, "no_context"));
    }

    [Fact]
    public async Task The_dhan_phase_goes_the_index_depth_back_for_an_index_without_bars_and_the_ordinary_depth_for_a_share()
    {
        var index = await SeedAsync("NIFTY", type: "index", dhanId: false);
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "INSERT INTO market.instrument_broker_ids (instrument_id, broker, broker_id, segment) VALUES (@id, 'dhan', '13', 'IDX_I')", ("id", index));
        }

        var abb = await SeedAsync("ABB");
        await TrackAsync([index, abb]);
        var source = Factory.Dhan.UseSource(t => new IntradayResult(t.InstrumentId, [], t.Windows.Count));
        Factory.Dhan.UseConnectedAccount();

        var job = await RunAsync(["dhan"]);

        job.Dhan.Status.ShouldBe("succeeded", job.Dhan.Message);
        var plan = source.Plans.ShouldHaveSingleItem();
        var nifty = plan.Targets.Single(t => t.Symbol == "NSE:NIFTY");
        nifty.Windows[0].From.ShouldBe(Now.AddDays(-1825), TimeSpan.FromSeconds(5));
        (nifty.Segment, nifty.Type).ShouldBe(("IDX_I", "index"));
        plan.Targets.Single(t => t.Symbol == "NSE:ABB").Windows[0].From.ShouldBe(Now.AddDays(-365), TimeSpan.FromSeconds(5));
    }

    private async Task TrackAsync(Guid[] ids)
    {
        using var response = await Client.PostJsonAsync("/api/instruments/tracked", new { instrumentIds = ids }, Ct);
        response.EnsureSuccessStatusCode();
    }
}
