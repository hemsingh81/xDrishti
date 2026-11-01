using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.OptionQuality;
using XDrishti.Application.OptionQuality;
using XDrishti.Domain.Options.Quality;

namespace XDrishti.Api.IntegrationTests.OptionQuality;

/// <summary>
/// P14-04 on a real TimescaleDB (criteria 2, 3, 7, 9): a synthetic recorded day and a synthetic backfill window are read through the quality reader, checked and stored; the India day boundary of the request count; a day with requests but no snapshot is
/// unavailable, never pass; a re-run checks nothing twice and writes a new run row; the newest run wins; one NIFTY-sized session is checked. The archive tables are filled by raw SQL of the test.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class OptionQualityDatabaseTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly DateOnly Session = new(2026, 10, 9);

    private static readonly DateOnly WindowFrom = new(2026, 9, 7);

    private const string Truncate = "TRUNCATE options.chain_snapshots, options.chain_requests, options.chain_sessions, options.rolling_bars, options.fetch_log, options.quality_results, options.quality_runs RESTART IDENTITY";

    private Guid _nifty;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, Truncate);
        _nifty = await SeedAsync("NIFTY", type: "index", dhanId: false);
    }

    public override async ValueTask DisposeAsync()
    {
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, Truncate);
        }

        await base.DisposeAsync();
    }

    private async Task<T> WithAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        using var scope = Factory.Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    private Task<OptionQualityRunSummary> RunAsync(string trigger = "manual") => WithAsync(async p => await p.GetRequiredService<OptionArchiveQualityRunner>().RunAsync(trigger, Ct));

    /// <summary>Two 5-minute slots of seven strikes on both sides, quoted so that put-call parity holds, with plausible Greeks; and the day's requests and end row.</summary>
    private async Task SeedChainDayAsync(int requests = 10, bool withSnapshots = true)
    {
        await using var connection = await OpenAsync();
        var open = new DateTimeOffset(2026, 10, 9, 9, 20, 0, TimeSpan.FromMinutes(330)).ToUniversalTime();
        if (withSnapshots)
        {
            await ExecuteAsync(
                connection,
                """
                INSERT INTO options.chain_snapshots (ts, underlying_id, expiry, strike_px, side, rx_at, security_id, underlying_px, last_px, bid_px, bid_qty, ask_px, ask_qty, oi, volume, iv, delta, gamma, vega, flags)
                SELECT ts, @u, @expiry, k, side, ts, (k / 10) + CASE WHEN side = 'CE' THEN 0 ELSE 1 END, 2500000, mid, mid - 100, 10, mid + 100, 10, 1000 + slot * 10, 100 + slot * 50, 20,
                       (0.5 - (k - 2500000) / 10000.0 * 0.05 - CASE WHEN side = 'PE' THEN 1 ELSE 0 END)::real, 0.001, 1.0, 0
                FROM (SELECT s.slot, @open::timestamptz + s.slot * interval '5 minutes' AS ts, k, sd.side,
                             CASE WHEN sd.side = 'CE' THEN 50000 ELSE 50000 - (2500000 - k) END AS mid
                      FROM generate_series(0, 1) s(slot), generate_series(2470000, 2530000, 10000) k, (VALUES ('CE'), ('PE')) sd(side)) x
                """,
                ("u", _nifty), ("expiry", Session), ("open", open));
        }

        for (var i = 0; i < requests; i++)
        {
            await ExecuteAsync(
                connection,
                "INSERT INTO options.chain_requests (slot_ts, kind, underlying_id, expiry, outcome, rows, latency_ms, requested_at) VALUES (@ts, 'chain', @u, @expiry, 'ok', 14, 100, @ts)",
                ("ts", open.AddMinutes(i)), ("u", _nifty), ("expiry", Session));
        }

        await ExecuteAsync(
            connection,
            "INSERT INTO options.chain_sessions (session_date, started_at, ended_at, end_reason, slots, requests, ok, gaps, rows) VALUES (@d, @s, @e, 'window_end', 2, @r, @r, 0, 28)",
            ("d", Session), ("s", open), ("e", open.AddHours(6)), ("r", requests));
    }

    /// <summary>One 5-minute session (75 bars) of a rolling series on 2026-09-07 inside a finished window, with the underlying's 1-minute closes at each bar's end minute.</summary>
    private async Task SeedRollingWindowAsync(int spotPx = 2_500_000)
    {
        await using var connection = await OpenAsync();
        var open = new DateTimeOffset(2026, 9, 7, 9, 15, 0, TimeSpan.FromMinutes(330)).ToUniversalTime();
        await ExecuteAsync(
            connection,
            """
            INSERT INTO options.rolling_bars (ts, underlying_id, expiry_flag, expiry_code, atm_offset, side, interval_minutes, strike_px, spot_px, open_px, high_px, low_px, close_px, flags)
            SELECT @open::timestamptz + i * interval '5 minutes', @u, 'W', 1, 0, 'CE', 5, 2500000, @spot, 100, 100, 100, 100, 0 FROM generate_series(0, 74) i
            """,
            ("open", open), ("u", _nifty), ("spot", spotPx));
        await ExecuteAsync(
            connection,
            "INSERT INTO market.bars_1m (instrument_id, ts, open, high, low, close, volume, source) SELECT @u, @open::timestamptz + (i * 5 + 4) * interval '1 minute', 25000, 25000, 25000, 25000, 1, 'dhan' FROM generate_series(0, 74) i",
            ("u", _nifty), ("open", open));
        await ExecuteAsync(
            connection,
            "INSERT INTO options.fetch_log (underlying_id, expiry_flag, expiry_code, atm_offset, side, interval_minutes, window_from, window_to, attempt, started_at, ended_at, outcome, http_status, bars, latency_ms) VALUES (@u, 'W', 1, 0, 'CE', 5, @from, @to, 1, @s, @s, 'ok', 200, 75, 100)",
            ("u", _nifty), ("from", WindowFrom), ("to", WindowFrom.AddDays(30)), ("s", open.AddDays(30)));
    }

    private async Task<Dictionary<string, (string Status, string? Reason)>> ChainChecksAsync(DateOnly day)
    {
        await using var connection = await OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand("SELECT check_code, status, reason FROM options.quality_results WHERE source = 'chain' AND session_date = @d", connection);
        command.Parameters.AddWithValue("d", day);
        var checks = new Dictionary<string, (string, string?)>();
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
        {
            checks[reader.GetString(0)] = (reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2));
        }

        return checks;
    }

    [Fact]
    public async Task A_recorded_day_round_trips_through_the_reader_the_checks_and_the_store_and_the_day_is_usable()
    {
        await SeedChainDayAsync();

        var summary = await RunAsync();

        summary.Sessions.ShouldBe(1);
        var checks = await ChainChecksAsync(Session);
        checks.Keys.Order().ShouldBe(["coverage", "greeks", "oi_volume", "parity"]);
        checks["coverage"].Status.ShouldBe("pass");
        checks["parity"].Status.ShouldBe("pass");
        checks["greeks"].Status.ShouldBe("pass");
        checks["oi_volume"].Status.ShouldBe("pass");
        (await ScalarAsync<long>("SELECT count(*) FROM options.quality_runs")).ShouldBe(1);
    }

    [Fact]
    public async Task The_requests_are_counted_by_india_day_a_request_at_23_59_belongs_to_the_day_and_one_at_00_01_to_the_next()
    {
        await SeedChainDayAsync(requests: 10);
        await using (var connection = await OpenAsync())
        {
            var late = new DateTimeOffset(2026, 10, 9, 23, 59, 0, TimeSpan.FromMinutes(330)).ToUniversalTime();
            var next = new DateTimeOffset(2026, 10, 10, 0, 1, 0, TimeSpan.FromMinutes(330)).ToUniversalTime();
            await ExecuteAsync(connection, "INSERT INTO options.chain_requests (slot_ts, kind, underlying_id, expiry, outcome, rows, latency_ms, requested_at) VALUES (@ts, 'chain', @u, @e, 'ok', 14, 100, @ts)", ("ts", late), ("u", _nifty), ("e", Session));
            await ExecuteAsync(connection, "INSERT INTO options.chain_requests (slot_ts, kind, underlying_id, expiry, outcome, rows, latency_ms, requested_at) VALUES (@ts, 'chain', @u, @e, 'timeout', 0, 100, @ts)", ("ts", next), ("u", _nifty), ("e", Session));
        }

        await RunAsync();

        (await ScalarAsync<int>("SELECT n FROM options.quality_results WHERE source = 'chain' AND check_code = 'coverage' AND session_date = @d", ("d", Session))).ShouldBe(11);
        (await ScalarAsync<decimal>("SELECT value FROM options.quality_results WHERE source = 'chain' AND check_code = 'coverage' AND session_date = @d", ("d", Session))).ShouldBe(1m);
    }

    [Fact]
    public async Task A_day_with_requests_but_no_snapshot_row_is_unavailable_in_every_data_check_and_never_pass()
    {
        await SeedChainDayAsync(withSnapshots: false);

        await RunAsync();

        var checks = await ChainChecksAsync(Session);
        checks["coverage"].Status.ShouldBe("pass");
        checks["parity"].ShouldBe(("unavailable", "no_two_sided_quote"));
        checks["greeks"].ShouldBe(("unavailable", "too_few_points"));
        checks["oi_volume"].ShouldBe(("unavailable", "too_few_points"));
    }

    [Fact]
    public async Task A_backfilled_window_is_checked_per_trading_day_and_key_with_the_underlying_close_at_the_bar_end()
    {
        await SeedRollingWindowAsync();

        await RunAsync();

        var key = await ScalarAsync<string>("SELECT DISTINCT fetch_key FROM options.quality_results WHERE source = 'rolling'");
        key.ShouldBe($"{_nifty}:W1:0:CE:5");
        (await ScalarAsync<string>("SELECT status FROM options.quality_results WHERE source = 'rolling' AND check_code = 'coverage' AND session_date = '2026-09-07'")).ShouldBe("pass");
        (await ScalarAsync<string>("SELECT status FROM options.quality_results WHERE source = 'rolling' AND check_code = 'stitching' AND session_date = '2026-09-07'")).ShouldBe("pass");
        (await ScalarAsync<string>("SELECT detail ->> 'spot_compared' FROM options.quality_results WHERE source = 'rolling' AND check_code = 'stitching' AND session_date = '2026-09-07'")).ShouldBe("75");
        (await ScalarAsync<long>("SELECT count(*) FROM options.quality_results WHERE source = 'rolling' AND session_date = '2026-09-08'")).ShouldBe(0); // no series of the underlying has a bar that day: not a session
        (await ScalarAsync<long>("SELECT count(*) FROM options.quality_results WHERE source = 'rolling' AND session_date IN ('2026-09-05', '2026-09-06')")).ShouldBe(0); // weekends are not sessions
    }

    [Fact]
    public async Task A_spot_far_from_the_underlying_close_is_counted_as_a_mismatch_and_warns()
    {
        await SeedRollingWindowAsync(spotPx: 2_600_000); // 4 % above the 25,000.00 close

        await RunAsync();

        (await ScalarAsync<string>("SELECT status FROM options.quality_results WHERE source = 'rolling' AND check_code = 'stitching' AND session_date = '2026-09-07'")).ShouldBe("warn");
        (await ScalarAsync<string>("SELECT detail ->> 'spot_mismatches' FROM options.quality_results WHERE source = 'rolling' AND check_code = 'stitching' AND session_date = '2026-09-07'")).ShouldBe("75");
    }

    [Fact]
    public async Task A_re_run_checks_nothing_again_but_writes_a_new_run_row_and_an_empty_archive_writes_no_result()
    {
        var empty = await RunAsync();
        await SeedChainDayAsync();
        await SeedRollingWindowAsync();
        var first = await RunAsync();
        var results = await ScalarAsync<long>("SELECT count(*) FROM options.quality_results");

        var second = await RunAsync("daily");

        empty.Results.ShouldBe(0);
        first.Results.ShouldBeGreaterThan(0);
        second.Results.ShouldBe(0);
        second.RunId.ShouldBeGreaterThan(first.RunId);
        (await ScalarAsync<long>("SELECT count(*) FROM options.quality_results")).ShouldBe(results);
        (await ScalarAsync<long>("SELECT count(*) FROM options.quality_runs")).ShouldBe(3);
        (await ScalarAsync<string>("SELECT string_agg(trigger, ',' ORDER BY id) FROM options.quality_runs")).ShouldBe("manual,manual,daily");
        (await ScalarAsync<long>("SELECT count(*) FROM options.quality_runs WHERE results = 0")).ShouldBe(2);
    }

    [Fact]
    public async Task The_newest_run_wins_when_the_results_are_read()
    {
        await SeedChainDayAsync();
        await RunAsync();
        var store = await WithAsync(async p => await Task.FromResult(p.GetRequiredService<IOptionQualityStore>()));
        var run = await store.StartRunAsync(Ct);
        await store.AddResultsAsync(run, [new OptionQualityRow(Session, _nifty, "chain", null, null, new QualityCheck("coverage", QualityStatus.Fail, 0.5, 0.95, "gap", 20, null))], Ct);

        var newest = await WithAsync(async p => await p.GetRequiredService<IOptionQualityStore>().ChainResultsAsync(_nifty, Session, Ct));

        newest.Count.ShouldBe(4);
        newest.Single(r => r.Check.Code == "coverage").Check.Status.ShouldBe(QualityStatus.Fail);
        newest.Single(r => r.Check.Code == "parity").Check.Status.ShouldBe(QualityStatus.Pass);
    }

    [Fact]
    public async Task One_nifty_sized_session_is_read_by_one_bounded_query_and_checked()
    {
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(
                connection,
                """
                INSERT INTO options.chain_snapshots (ts, underlying_id, expiry, strike_px, side, rx_at, security_id, underlying_px, last_px, bid_px, bid_qty, ask_px, ask_qty, oi, volume, iv, delta, gamma, vega, flags)
                SELECT ts, @u, @expiry + e, k, side, ts, (e::bigint * 1000000) + (k / 10) + CASE WHEN side = 'CE' THEN 0 ELSE 1 END, 2500000, mid, mid - 100, 10, mid + 100, 10, 1000 + slot * 10, 100 + slot * 50, 20,
                       (0.5 - (k - 2500000) / 10000.0 * 0.0002 - CASE WHEN side = 'PE' THEN 1 ELSE 0 END)::real, 0.001, 1.0, 0
                FROM (SELECT s.slot, e, @open::timestamptz + s.slot * interval '5 minutes' AS ts, k, sd.side,
                             CASE WHEN sd.side = 'CE' THEN 400000 ELSE 400000 - (2500000 - k) END AS mid
                      FROM generate_series(0, 23) s(slot), generate_series(0, 1) e, generate_series(2380000, 2620000, 1000) k, (VALUES ('CE'), ('PE')) sd(side)) x
                """,
                ("u", _nifty), ("expiry", Session), ("open", new DateTimeOffset(2026, 10, 9, 9, 20, 0, TimeSpan.FromMinutes(330)).ToUniversalTime()));
            await ExecuteAsync(connection, "INSERT INTO options.chain_requests (slot_ts, kind, underlying_id, expiry, outcome, rows, latency_ms, requested_at) VALUES ('2026-10-09 04:00+00', 'chain', @u, @e, 'ok', 1, 1, now())", ("u", _nifty), ("e", Session));
            await ExecuteAsync(connection, "INSERT INTO options.chain_sessions (session_date, started_at, ended_at, end_reason, slots, requests, ok, gaps, rows) VALUES (@d, now(), now(), 'window_end', 24, 1, 1, 0, 1)", ("d", Session));
        }

        (await ScalarAsync<long>("SELECT count(*) FROM options.chain_snapshots")).ShouldBeGreaterThan(23_000);
        var points = await WithAsync(async p => await p.GetRequiredService<IOptionArchiveReader>().ChainPointsAsync(_nifty, Session, Ct));

        await RunAsync();

        points.Count.ShouldBe(23_136); // the whole day of one underlying comes back from one range read, none of another day or underlying
        (await ChainChecksAsync(Session)).Count.ShouldBe(4);
    }

    [Fact]
    public async Task A_crashed_day_with_requests_and_an_end_row_of_zero_is_checked_and_a_day_with_requests_and_no_end_row_too_and_neither_is_reported_as_not_recorded()
    {
        await SeedChainDayAsync(requests: 10);
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "UPDATE options.chain_sessions SET requests = 0, ok = 0, end_reason = 'crashed'");
            var thursday = new DateTimeOffset(2026, 10, 8, 9, 20, 0, TimeSpan.FromMinutes(330)).ToUniversalTime();
            await ExecuteAsync(connection, "INSERT INTO options.chain_requests (slot_ts, kind, underlying_id, expiry, outcome, rows, latency_ms, requested_at) VALUES (@ts, 'chain', @u, @e, 'ok', 14, 100, @ts)", ("ts", thursday), ("u", _nifty), ("e", Session));
        }

        await RunAsync();

        (await ChainChecksAsync(Session))["coverage"].Status.ShouldBe("pass");
        (await ChainChecksAsync(new DateOnly(2026, 10, 8)))["coverage"].Status.ShouldBe("pass");
        (await ScalarAsync<long>("SELECT count(*) FROM options.quality_results WHERE reason = 'not_recorded'")).ShouldBe(0);
    }

    [Fact]
    public async Task A_day_inside_the_lookback_with_no_request_at_all_is_not_recorded_for_the_underlyings_that_were_recorded()
    {
        await SeedChainDayAsync();
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "UPDATE options.chain_requests SET slot_ts = slot_ts - interval '3 days', requested_at = requested_at - interval '3 days'");
        }

        await RunAsync();

        (await ChainChecksAsync(new DateOnly(2026, 10, 8)))["coverage"].ShouldBe(("unavailable", "not_recorded"));
        (await ChainChecksAsync(new DateOnly(2026, 10, 9)))["coverage"].ShouldBe(("unavailable", "not_recorded"));
    }

    [Fact]
    public async Task The_strike_step_of_a_2021_window_is_not_taken_from_a_2026_contract()
    {
        var share = await SeedAsync("STEPSH");
        await using (var connection = await OpenAsync())
        {
            foreach (var strike in new[] { 100_000, 105_000, 110_000 })
            {
                await ExecuteAsync(connection, "INSERT INTO options.contracts (security_id, underlying_id, instrument, expiry, strike_px, side, lot_size_seen, first_seen_on, last_seen_on) VALUES (@s, @u, 'OPTSTK', '2026-11-26', @k, 'CE', 100, '2026-11-01', '2026-11-01')", ("s", (long)strike), ("u", share), ("k", strike));
                await ExecuteAsync(connection, "INSERT INTO options.contracts (security_id, underlying_id, instrument, expiry, strike_px, side, lot_size_seen, first_seen_on, last_seen_on) VALUES (@s, @u, 'OPTIDX', '2026-11-26', @k, 'CE', 100, '2026-11-01', '2026-11-01')", ("s", (long)strike + 1_000_000), ("u", _nifty), ("k", strike));
            }
        }

        var (old2021, near2026, indexOld, indexNear) = await WithAsync(async p =>
        {
            var reference = p.GetRequiredService<IOptionReference>();
            return (await reference.StrikeStepAsync(share, new DateOnly(2021, 12, 6), Ct), await reference.StrikeStepAsync(share, new DateOnly(2026, 11, 2), Ct), await reference.StrikeStepAsync(_nifty, new DateOnly(2021, 12, 6), Ct), await reference.StrikeStepAsync(_nifty, new DateOnly(2026, 11, 2), Ct));
        });

        old2021.ShouldBeNull(); // a share has no known step that long ago: stitching says no_strike_step
        near2026.ShouldBe(5_000);
        indexOld.ShouldBe(5_000); // the index fallback (50 rupees)
        indexNear.ShouldBe(5_000);
    }

    [Fact]
    public async Task A_past_holiday_on_which_no_series_has_a_bar_gets_no_verdict_while_a_day_the_key_missed_but_another_series_traded_is_a_fail()
    {
        await SeedRollingWindowAsync();
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(
                connection,
                "INSERT INTO options.rolling_bars (ts, underlying_id, expiry_flag, expiry_code, atm_offset, side, interval_minutes, strike_px, spot_px, open_px, high_px, low_px, close_px, flags) VALUES ('2026-09-09 04:00+00', @u, 'W', 1, 1, 'CE', 5, 2505000, 2500000, 100, 100, 100, 100, 0)",
                ("u", _nifty));
        }

        await RunAsync();

        (await ScalarAsync<long>("SELECT count(*) FROM options.quality_results WHERE source = 'rolling' AND session_date = '2026-09-08'")).ShouldBe(0); // nobody traded
        (await ScalarAsync<string>("SELECT status FROM options.quality_results WHERE source = 'rolling' AND check_code = 'coverage' AND session_date = '2026-09-09'")).ShouldBe("fail"); // another series did
    }

    private static readonly string[] RollingChecks = ["coverage", "stitching"];

    [Fact]
    public async Task The_rolling_read_is_limited_by_key_sessions_and_never_returns_half_of_one()
    {
        var store = await WithAsync(async p => await Task.FromResult(p.GetRequiredService<IOptionQualityStore>()));
        var run = await store.StartRunAsync(Ct);
        var rows = Enumerable.Range(0, 3).SelectMany(i => RollingChecks.Select(code => new OptionQualityRow(new DateOnly(2026, 9, 14).AddDays(i), _nifty, "rolling", null, "key", new QualityCheck(code, QualityStatus.Pass, 1, 1, null, 1, null)))).ToList();
        await store.AddResultsAsync(run, rows, Ct);

        var read = await store.RollingResultsAsync(_nifty, new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 16), 2, Ct);

        read.Count.ShouldBe(4);
        read.GroupBy(r => r.SessionDate).ShouldAllBe(g => g.Count() == 2);
    }
}
