using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Market.Context;
using XDrishti.Domain.Selection.SmartRules;

namespace XDrishti.Api.IntegrationTests.Data;

/// <summary>
/// P3-01e end to end on the real database: the breadth of the owner's tracked shares is set once per context day by the upkeep of the data job, from the stored daily bars up to that day, the table refuses every other update, the rebuild is the only way a
/// figure changes, the coverage tells the truth and the plan's market gate gets the VIX and the breadth in its own unit. The clock is pinned to Monday 2026-10-12 12:00 India time; the last closed session is Friday 2026-10-09.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class DataBreadthTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly DateOnly Monday = new(2026, 10, 12);

    private static readonly DateOnly[] ContextDays = [new(2026, 10, 5), new(2026, 10, 6), new(2026, 10, 7), new(2026, 10, 8), LastSession];

    /// <summary>The 25 sessions ending on the last closed one (every weekday: the test calendar has no holiday).</summary>
    private static readonly DateOnly[] Sessions = Enumerable.Range(0, 40).Select(i => LastSession.AddDays(-i)).Where(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).Take(25).Reverse().ToArray();

    private async Task InsertDailyAsync(Guid id, IEnumerable<DateOnly> dates, Func<int, double> close)
    {
        var list = dates.ToList();
        await using var connection = await OpenAsync();
        await ExecuteAsync(
            connection,
            """
            INSERT INTO market.bars_agg (instrument_id, timeframe, ts, open, high, low, close, volume, bars, complete)
            SELECT @id, '1D', (d::timestamp AT TIME ZONE 'Asia/Kolkata'), c, c, c, c, 1000, 375, true
            FROM unnest(@dates::date[], @closes::float8[]) AS t(d, c)
            """,
            ("id", id), ("dates", list.ToArray()), ("closes", list.Select((_, i) => close(i)).ToArray()));
    }

    private async Task<Guid> ShareAsync(string ticker, Func<int, double> close, IEnumerable<DateOnly>? dates = null, string type = "share", bool retired = false)
    {
        var id = await SeedAsync(ticker, type: type, retired: retired);
        await InsertDailyAsync(id, dates ?? Sessions, close);
        return id;
    }

    private async Task TrackAsync(params Guid[] ids)
    {
        using var response = await Client.PostJsonAsync("/api/instruments/tracked", new { instrumentIds = ids }, Ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task InsertContextRowAsync(DateOnly date, double? vix = 15, double? breadth = null, int? universe = null)
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(
            connection,
            """
            INSERT INTO market.context_daily (date, index_instrument_id, index_high, index_low, index_close, vix_close, index_bars, vix_bars, source, written_at, breadth, breadth_voters, breadth_universe, breadth_excluded, breadth_state, breadth_computed_at)
            VALUES (@d, gen_random_uuid(), 22010, 21990, 22000, @v::float8, 375, 375, 'derived_1m', now(), @b::float8, @u::smallint, @u::smallint, 0, CASE WHEN @b::float8 IS NULL THEN NULL ELSE 'value' END, CASE WHEN @b::float8 IS NULL THEN NULL ELSE now() END)
            """,
            ("d", date), ("v", (object?)vix ?? DBNull.Value), ("b", (object?)breadth ?? DBNull.Value), ("u", (object?)(short?)universe ?? DBNull.Value));
    }

    private async Task<string> BreadthDumpAsync() =>
        await ScalarAsync<string>("SELECT coalesce(string_agg(date::text || '|' || coalesce(breadth::text, '-') || '|' || coalesce(breadth_voters::text, '-') || '|' || coalesce(breadth_universe::text, '-') || '|' || coalesce(breadth_state, '-') || '|' || coalesce(breadth_computed_at::text, '-'), ',' ORDER BY date), '') FROM market.context_daily");

    private async Task<ContextCoverageView> CoverageAsync()
    {
        using var response = await Client.GetAsync("/api/data/context", Ct);
        return await response.ReadAsync<ContextCoverageView>(Ct);
    }

    /// <summary>Three shares above their 20-day average on the last day and one below; an ETF, a retired share and an untracked share that must not count.</summary>
    private async Task<Guid[]> SeedFourSharesAsync()
    {
        var up1 = await ShareAsync("UPONE", i => 100 + i);
        var up2 = await ShareAsync("UPTWO", i => 50 + i * 2);
        var up3 = await ShareAsync("UPTHREE", i => 10 + i * 0.5);
        var down = await ShareAsync("DOWN", i => 200 - i);
        var etf = await ShareAsync("NIFTYBEES", i => 200 - i, type: "etf");
        var retired = await ShareAsync("GONE", i => 200 - i, retired: true);
        await ShareAsync("UNTRACKED", i => 200 - i);
        await TrackAsync(up1, up2, up3, down, etf);
        _ = retired;
        foreach (var day in ContextDays)
        {
            await InsertContextRowAsync(day);
        }

        return [up1, up2, up3, down];
    }

    [Fact]
    public async Task The_upkeep_of_a_data_job_sets_the_breadth_of_the_tracked_shares_only_and_the_endpoint_shows_it_with_the_honest_label()
    {
        await SeedFourSharesAsync();

        var job = await RunAsync(["aggregate"]);

        job.Status.ShouldBe("succeeded");
        var coverage = await CoverageAsync();
        var b = coverage.Breadth;
        (b.LatestDate, b.Latest, b.State, b.Voters, b.Universe, b.Excluded).ShouldBe((LastSession, 75.0, "value", 4, 4, 0));
        (b.ComputedDays, b.PendingDays, b.InsufficientDays).ShouldBe((5, 0, 0));
        b.Label.ShouldBe("breadth of your 4 tracked shares (members as of today), not of the market");
        (await ScalarAsync<long>("SELECT count(*) FROM market.context_daily WHERE breadth_state = 'value' AND breadth_computed_at IS NOT NULL")).ShouldBe(5);
    }

    [Fact]
    public async Task A_second_run_after_the_stock_bars_changed_leaves_every_stored_breadth_as_it_was()
    {
        await SeedFourSharesAsync();
        await RunAsync(["aggregate"]);
        var before = await BreadthDumpAsync();
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "UPDATE market.bars_agg SET close = 1, open = 1, high = 1, low = 1 WHERE timeframe = '1D'");
        }

        Factory.Clock.Pin(Now.AddHours(2));
        await ResignInAsync();
        await RunAsync(["aggregate"]);

        (await BreadthDumpAsync()).ShouldBe(before);
    }

    [Fact]
    public async Task A_share_with_no_bar_on_the_last_closed_session_leaves_that_day_waiting_and_a_share_with_an_action_in_the_window_does_not_vote()
    {
        var ids = await SeedFourSharesAsync();
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "DELETE FROM market.bars_agg WHERE instrument_id = @id AND ts >= @from", ("id", ids[3]), ("from", new DateTimeOffset(2026, 10, 8, 18, 30, 0, TimeSpan.Zero)));
            await ExecuteAsync(
                connection,
                "INSERT INTO market.corporate_actions (id, instrument_id, ex_date, kind, numerator, denominator, factor, status, source, created_at) VALUES (gen_random_uuid(), @id, @ex, 'split', 1, 5, 0.2, 'suggested', 'detected', now())",
                ("id", ids[0]), ("ex", new DateOnly(2026, 10, 1)));
        }

        await RunAsync(["aggregate"]);

        var b = (await CoverageAsync()).Breadth;
        (b.ComputedDays, b.PendingDays, b.LatestDate).ShouldBe((4, 1, ContextDays[3]));
        (await ScalarAsync<long>("SELECT count(*) FROM market.context_daily WHERE date = @d AND breadth_state IS NULL", ("d", LastSession))).ShouldBe(1);
        (b.Voters, b.Universe, b.Excluded).ShouldBe((3, 4, 1), "the share with the split inside the window is counted as excluded, and on 2026-10-08 the three others vote");
    }

    [Fact]
    public async Task A_thin_sample_is_stored_as_insufficient_with_its_counts_and_the_coverage_names_the_date()
    {
        var one = await ShareAsync("ONLY", i => 100 + i);
        var two = await ShareAsync("SECOND", i => 100 + i);
        await TrackAsync(one, two);
        await InsertContextRowAsync(ContextDays[0]);

        await RunAsync(["aggregate"]);

        var b = (await CoverageAsync()).Breadth;
        (b.State, b.Latest, b.Voters, b.Universe, b.InsufficientDays).ShouldBe(("insufficient", null, 2, 2, 1));
        b.InsufficientDates.ShouldBe([ContextDays[0]]);
    }

    [Fact]
    public async Task The_rebuild_previews_by_default_and_with_write_replaces_the_stored_figures_and_the_context_reader_sees_them()
    {
        await SeedFourSharesAsync();
        await RunAsync(["aggregate"]);
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "UPDATE market.bars_agg SET close = 1, open = 1, high = 1, low = 1 WHERE timeframe = '1D' AND instrument_id IN (SELECT id FROM market.instruments WHERE display_name LIKE 'UPONE%' OR display_name LIKE 'UPTWO%') AND ts >= @from", ("from", new DateTimeOffset(2026, 10, 8, 18, 30, 0, TimeSpan.Zero)));
        }

        using var scope = Factory.Services.CreateScope();
        var rebuild = scope.ServiceProvider.GetRequiredService<ICommandHandler<RebuildBreadthCommand, BreadthRebuilt>>();
        var before = await BreadthDumpAsync();

        var preview = await rebuild.HandleAsync(new RebuildBreadthCommand(ContextDays[0], LastSession, false), Ct);

        preview.Value.Changes.Select(c => (c.Date, c.OldValue, c.NewValue)).ShouldBe([(LastSession, (double?)75.0, (double?)25.0)]);
        (await BreadthDumpAsync()).ShouldBe(before, "a dry run writes nothing");

        var written = await rebuild.HandleAsync(new RebuildBreadthCommand(ContextDays[0], LastSession, true), Ct);

        written.Value.Written.ShouldBeTrue();
        (await ScalarAsync<double>("SELECT breadth FROM market.context_daily WHERE date = @d", ("d", LastSession))).ShouldBe(25.0);
        (await ScalarAsync<double>("SELECT breadth FROM market.context_daily WHERE date = @d", ("d", ContextDays[0]))).ShouldBe(75.0, "unchanged days keep their figure");
        var figures = scope.ServiceProvider.GetRequiredService<XDrishti.Application.Abstractions.Market.Context.IContextFigures>();
        (await figures.AsOfAsync(Monday, Ct)).Figures!.Breadth.ShouldBe(25.0, "the reader does not serve a context cached before the rebuild");
    }

    [Fact]
    public async Task The_plan_context_gets_the_vix_and_the_breadth_in_the_unit_of_the_gate_and_no_figures_leave_the_gate_unavailable()
    {
        using (var scope = Factory.Services.CreateScope())
        {
            var source = scope.ServiceProvider.GetRequiredService<IPlanContextSource>();
            (await source.ReadAsync(new PlanContextRequest(LastSession, Monday, []), Ct)).Value.Gate.ShouldBeNull();
        }

        await InsertContextRowAsync(LastSession, vix: 19.4, breadth: 27.5, universe: 214);
        using var later = Factory.Services.CreateScope();

        var ctx = (await later.ServiceProvider.GetRequiredService<IPlanContextSource>().ReadAsync(new PlanContextRequest(LastSession, Monday, []), Ct)).Value;

        ctx.Gate.ShouldBe(new MarketGateInput(LastSession, 19.4, 0.275, null));
        ctx.MarketNote.ShouldBe("Breadth of your 214 tracked shares (members as of today), not of the market.");
    }

    [Fact]
    public async Task The_back_fill_of_1100_days_for_214_shares_is_computed_from_one_read_per_share_inside_three_seconds()
    {
        var ids = new List<Guid>();
        for (var i = 0; i < 214; i++)
        {
            ids.Add(await SeedAsync($"S{i:D3}"));
        }

        await TrackAsync([.. ids]);
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(
                connection,
                """
                WITH days AS (SELECT d::date AS d FROM generate_series(date '2022-06-01', date '2026-10-09', interval '1 day') g(d) WHERE extract(isodow FROM d) < 6)
                INSERT INTO market.bars_agg (instrument_id, timeframe, ts, open, high, low, close, volume, bars, complete)
                SELECT i, '1D', (d::timestamp AT TIME ZONE 'Asia/Kolkata'), p, p, p, p, 1000, 375, true
                FROM unnest(@ids) AS i, days, LATERAL (SELECT 100 + random() * 50 AS p) AS x
                """,
                ("ids", ids.ToArray()));
            await ExecuteAsync(
                connection,
                """
                INSERT INTO market.context_daily (date, index_instrument_id, index_high, index_low, index_close, vix_close, index_bars, vix_bars, source, written_at)
                SELECT d::date, gen_random_uuid(), 22010, 21990, 22000, 15, 375, 375, 'derived_1m', now()
                FROM (SELECT d FROM generate_series(date '2022-06-01', date '2026-10-09', interval '1 day') g(d) WHERE extract(isodow FROM d) < 6 OFFSET 20) t
                """);
        }

        var days = await ScalarAsync<long>("SELECT count(*) FROM market.context_daily");
        using var scope = Factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<ComputeBreadthCommand, BreadthComputed>>();
        var clock = Stopwatch.StartNew();

        var result = await handler.HandleAsync(new ComputeBreadthCommand(), Ct);

        clock.Stop();
        days.ShouldBeGreaterThan(1_100);
        result.Value.Computed.ShouldBe((int)days);
        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(3));
    }
}
