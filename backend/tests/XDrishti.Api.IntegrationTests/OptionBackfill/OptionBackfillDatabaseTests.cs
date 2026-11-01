using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.OptionBackfill;
using XDrishti.Application.Abstractions.Options;
using XDrishti.Infrastructure.OptionBackfill;

namespace XDrishti.Api.IntegrationTests.OptionBackfill;

/// <summary>
/// P14-03 on a real TimescaleDB (criteria 3, 4, 7, 8): the hypertable and its compression settings, a synthetic window that compresses and reads back identically, the idempotent re-insert of a window, a failed attempt that has a log row and no bar, the resume state
/// the log gives back, the constraints and the switch and acknowledgement rows. Bars are only ever read here by raw SQL of the test.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class OptionBackfillDatabaseTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly Guid Underlying = Guid.Parse("00000000-0000-0000-0000-0000000000d1");

    private static readonly DateOnly From = new(2026, 9, 2);

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "TRUNCATE options.rolling_bars, options.fetch_log");
        await ExecuteAsync(connection, "TRUNCATE options.backfill_switch, options.backfill_probe_ack RESTART IDENTITY");
    }

    private async Task<T> WithAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        using var scope = Factory.Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    private static RollingBarStore Store(IServiceProvider p) => new(p.GetRequiredService<NpgsqlDataSource>());

    private static BackfillBar Bar(DateTimeOffset ts, int offset = 10, string side = "CE") =>
        new(ts, Underlying, 'W', 1, offset, side, 5, 2_550_000, 2_501_230, 4_820, 5_230, 4_790, 5_105, 128_450, 1_523_000, 12.31f, 0);

    private static BackfillFetchRecord Attempt(string outcome = "ok", int attempt = 1, int bars = 0, DateOnly? from = null) =>
        new(new BackfillKey(Underlying, 'W', 1, 10, "CE", 5, from ?? From), attempt, new DateTimeOffset(2026, 10, 12, 20, 0, 0, TimeSpan.FromMinutes(330)), new DateTimeOffset(2026, 10, 12, 20, 0, 1, TimeSpan.FromMinutes(330)), outcome, outcome == "ok" ? 200 : 503, outcome == "ok" ? null : "DH-908", bars, 120);

    private static IEnumerable<BackfillBar> Day(DateTimeOffset start, int offset = 10, string side = "CE") =>
        Enumerable.Range(0, 75).Select(i => Bar(start.AddMinutes(5 * i), offset, side));

    [Fact]
    public async Task The_bar_table_is_a_thirty_day_hypertable_compressed_after_seven_days_by_series_with_no_retention()
    {
        (await ScalarAsync<string>("SELECT time_interval::text FROM timescaledb_information.dimensions WHERE hypertable_schema = 'options' AND hypertable_name = 'rolling_bars'")).ShouldBe("30 days");
        (await ScalarAsync<bool>("SELECT compression_enabled FROM timescaledb_information.hypertables WHERE hypertable_schema = 'options' AND hypertable_name = 'rolling_bars'")).ShouldBeTrue();
        (await ScalarAsync<string>("SELECT string_agg(attname, ',' ORDER BY segmentby_column_index) FROM timescaledb_information.compression_settings WHERE hypertable_schema = 'options' AND hypertable_name = 'rolling_bars' AND segmentby_column_index IS NOT NULL")).ShouldBe("underlying_id,expiry_flag,expiry_code,side");
        (await ScalarAsync<string>("SELECT string_agg(attname, ',' ORDER BY orderby_column_index) FROM timescaledb_information.compression_settings WHERE hypertable_schema = 'options' AND hypertable_name = 'rolling_bars' AND orderby_column_index IS NOT NULL")).ShouldBe("atm_offset,ts");
        (await ScalarAsync<string>("SELECT (config ->> 'compress_after') FROM timescaledb_information.jobs WHERE proc_name = 'policy_compression' AND hypertable_name = 'rolling_bars'")).ShouldBe("7 days");
        (await ScalarAsync<long>("SELECT count(*) FROM timescaledb_information.jobs WHERE hypertable_name = 'rolling_bars' AND proc_name = 'policy_retention'")).ShouldBe(0);
        (await ScalarAsync<string>("SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = 'pk_rolling_bars'")).ShouldBe("PRIMARY KEY (ts, underlying_id, expiry_flag, expiry_code, atm_offset, side, interval_minutes)");
    }

    [Fact]
    public async Task A_synthetic_window_round_trips_through_copy_and_compression_identically()
    {
        var old = new DateTimeOffset(2021, 11, 15, 9, 15, 0, TimeSpan.FromMinutes(330));
        var bars = Day(old).Concat(Day(old, 9)).Concat(Day(old, 10, "PE")).ToList();

        var written = await WithAsync(async p => await Store(p).AppendAsync(Attempt(bars: bars.Count), bars, Ct));
        const string Digest = "SELECT md5(string_agg(concat_ws('|', ts, underlying_id, expiry_flag, expiry_code, atm_offset, side, interval_minutes, strike_px, spot_px, open_px, high_px, low_px, close_px, volume, oi, iv, flags), ';' ORDER BY atm_offset, side, ts)) FROM options.rolling_bars";
        var before = await ScalarAsync<string>(Digest);
        (await ScalarAsync<long>("SELECT count(*) FROM (SELECT compress_chunk(c) FROM show_chunks('options.rolling_bars') c) x")).ShouldBeGreaterThan(0);
        var after = await ScalarAsync<string>(Digest);

        written.ShouldBe(225);
        after.ShouldBe(before);
        (await ScalarAsync<long>("SELECT count(*) FROM timescaledb_information.chunks WHERE hypertable_name = 'rolling_bars' AND is_compressed")).ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task A_window_fetched_twice_inserts_no_bar_the_second_time_and_both_attempts_are_logged()
    {
        var ts = new DateTimeOffset(2026, 9, 4, 9, 15, 0, TimeSpan.FromMinutes(330));
        BackfillBar[] bars = [Bar(ts), Bar(ts.AddMinutes(5))];

        var first = await WithAsync(async p => await Store(p).AppendAsync(Attempt(bars: 2), bars, Ct));
        var second = await WithAsync(async p => await Store(p).AppendAsync(Attempt(attempt: 2, bars: 2), bars, Ct));

        (first, second).ShouldBe((2, 0));
        (await ScalarAsync<long>("SELECT count(*) FROM options.rolling_bars")).ShouldBe(2);
        (await ScalarAsync<long>("SELECT count(*) FROM options.fetch_log")).ShouldBe(2);
    }

    [Fact]
    public async Task A_missing_value_is_stored_as_null_never_as_a_zero_and_a_zero_close_is_kept_with_the_flag()
    {
        var ts = new DateTimeOffset(2026, 9, 4, 9, 15, 0, TimeSpan.FromMinutes(330));
        var bar = Bar(ts) with { Volume = null, Oi = null, Iv = null, ClosePx = 0, Flags = BackfillFlags.Suspect };

        await WithAsync(async p => await Store(p).AppendAsync(Attempt(bars: 1), [bar], Ct));

        (await ScalarAsync<long>("SELECT count(*) FROM options.rolling_bars WHERE volume IS NULL AND oi IS NULL AND iv IS NULL AND close_px = 0 AND flags = 1")).ShouldBe(1);
    }

    [Fact]
    public async Task A_failed_attempt_has_a_log_row_and_no_bar()
    {
        await WithAsync(async p => await Store(p).AppendAsync(Attempt("server_error"), [], Ct));

        (await ScalarAsync<long>("SELECT count(*) FROM options.rolling_bars")).ShouldBe(0);
        (await ScalarAsync<string>("SELECT outcome || ':' || http_status || ':' || dhan_code || ':' || bars || ':' || window_from::text || ':' || window_to::text FROM options.fetch_log")).ShouldBe("server_error:503:DH-908:0:2026-09-02:2026-10-02");
    }

    [Fact]
    public async Task The_fetch_log_gives_back_the_resume_state_done_given_up_and_the_requests_since_a_time()
    {
        var done = From;
        var failing = From.AddDays(-30);
        var refused = From.AddDays(-60);
        await WithAsync(async p =>
        {
            var store = Store(p);
            await store.AppendAsync(Attempt("ok", 1, 1, done), [Bar(new DateTimeOffset(2026, 9, 4, 9, 15, 0, TimeSpan.FromMinutes(330)))], Ct);
            await store.AppendAsync(Attempt("timeout", 1, from: failing), [], Ct);
            await store.AppendAsync(Attempt("server_error", 2, from: failing), [], Ct);
            await store.AppendAsync(Attempt("bad_body", 3, from: failing), [], Ct);
            await store.AppendAsync(Attempt("token_refused", 1, from: refused), [], Ct);
            await store.AppendAsync(Attempt("disk_stop", 2, from: refused), [], Ct);
            return 0;
        });

        var log = new BackfillFetchLog(Factory.Services.GetRequiredService<NpgsqlDataSource>());
        var state = await log.LoadStateAsync(Ct);

        state[new BackfillKey(Underlying, 'W', 1, 10, "CE", 5, done)].ShouldBe(new KeyState(1, 0, "ok"));
        state[new BackfillKey(Underlying, 'W', 1, 10, "CE", 5, failing)].ShouldSatisfyAllConditions(s => s.Failed.ShouldBe(3), s => s.IsGivenUp.ShouldBeTrue());
        state[new BackfillKey(Underlying, 'W', 1, 10, "CE", 5, refused)].ShouldBe(new KeyState(2, 0, null));
        (await log.CountSentSinceAsync(new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.FromMinutes(330)), Ct)).ShouldBe(5, "the disk stop was never sent");
        (await log.CountSentSinceAsync(new DateTimeOffset(2026, 10, 13, 0, 0, 0, TimeSpan.FromMinutes(330)), Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task The_same_attempt_number_of_a_key_cannot_be_logged_twice()
    {
        await WithAsync(async p => await Store(p).AppendAsync(Attempt(), [], Ct));

        await Should.ThrowAsync<PostgresException>(async () => await WithAsync(async p => await Store(p).AppendAsync(Attempt(), [], Ct)));
    }

    [Theory]
    [InlineData("INSERT INTO options.rolling_bars (ts, underlying_id, expiry_flag, expiry_code, atm_offset, side, interval_minutes, strike_px, spot_px, open_px, high_px, low_px, close_px, flags) VALUES (now(), gen_random_uuid(), 'X', 1, 0, 'CE', 5, 1, 1, 1, 1, 1, 1, 0)")]
    [InlineData("INSERT INTO options.rolling_bars (ts, underlying_id, expiry_flag, expiry_code, atm_offset, side, interval_minutes, strike_px, spot_px, open_px, high_px, low_px, close_px, flags) VALUES (now(), gen_random_uuid(), 'W', 1, 11, 'CE', 5, 1, 1, 1, 1, 1, 1, 0)")]
    [InlineData("INSERT INTO options.rolling_bars (ts, underlying_id, expiry_flag, expiry_code, atm_offset, side, interval_minutes, strike_px, spot_px, open_px, high_px, low_px, close_px, flags) VALUES (now(), gen_random_uuid(), 'W', 1, 0, 'XX', 5, 1, 1, 1, 1, 1, 1, 0)")]
    [InlineData("INSERT INTO options.rolling_bars (ts, underlying_id, expiry_flag, expiry_code, atm_offset, side, interval_minutes, strike_px, spot_px, open_px, high_px, low_px, close_px, flags) VALUES (now(), gen_random_uuid(), 'W', 1, 0, 'CE', 7, 1, 1, 1, 1, 1, 1, 0)")]
    [InlineData("INSERT INTO options.rolling_bars (ts, underlying_id, expiry_flag, expiry_code, atm_offset, side, interval_minutes, strike_px, spot_px, open_px, high_px, low_px, close_px, volume, flags) VALUES (now(), gen_random_uuid(), 'W', 1, 0, 'CE', 5, 1, 1, 1, 1, 1, 1, -1, 0)")]
    [InlineData("INSERT INTO options.rolling_bars (ts, underlying_id, expiry_flag, expiry_code, atm_offset, side, interval_minutes, strike_px, spot_px, open_px, high_px, low_px, close_px, flags) VALUES (now(), gen_random_uuid(), 'W', 1, 0, 'CE', 5, 1, 1, 1, 1, 1, 1, 2)")]
    [InlineData("INSERT INTO options.fetch_log (underlying_id, expiry_flag, expiry_code, atm_offset, side, interval_minutes, window_from, window_to, attempt, started_at, ended_at, outcome, bars, latency_ms) VALUES (gen_random_uuid(), 'W', 1, 0, 'CE', 5, '2026-09-01', '2026-10-01', 1, now(), now(), 'slot_overrun', 0, 0)")]
    [InlineData("INSERT INTO options.fetch_log (underlying_id, expiry_flag, expiry_code, atm_offset, side, interval_minutes, window_from, window_to, attempt, started_at, ended_at, outcome, bars, latency_ms) VALUES (gen_random_uuid(), 'W', 1, 0, 'CE', 5, '2026-10-01', '2026-09-01', 1, now(), now(), 'ok', 0, 0)")]
    [InlineData("INSERT INTO options.fetch_log (underlying_id, expiry_flag, expiry_code, atm_offset, side, interval_minutes, window_from, window_to, attempt, started_at, ended_at, outcome, bars, latency_ms) VALUES (gen_random_uuid(), 'W', 1, 0, 'CE', 5, '2026-09-01', '2026-10-01', 1, now(), now(), 'made_up', 0, 0)")]
    [InlineData("INSERT INTO options.backfill_switch (enabled, actor, reason) VALUES (true, 'service:x', 'r')")]
    [InlineData("INSERT INTO options.backfill_switch (enabled, actor, reason) VALUES (true, 'owner:x@example.invalid', '  ')")]
    [InlineData("INSERT INTO options.backfill_probe_ack (actor, note) VALUES ('owner:x@example.invalid', ' ')")]
    [InlineData("INSERT INTO options.backfill_probe_ack (actor, note) VALUES ('nobody', 'x')")]
    public async Task The_database_refuses_a_row_outside_the_checks(string sql)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        var failure = await Should.ThrowAsync<PostgresException>(async () => await command.ExecuteNonQueryAsync(Ct));

        failure.SqlState.ShouldBe("23514");
    }

    [Fact]
    public async Task The_switch_and_the_acknowledgement_append_and_read_back_newest_first_and_start_empty()
    {
        await WithAsync(async p =>
        {
            var source = p.GetRequiredService<NpgsqlDataSource>();
            var switches = new OptionBackfillSwitchStore(source);
            var acks = new BackfillProbeAckStore(source);

            (await switches.CurrentAsync(Ct)).ShouldBeNull("no row = OFF");
            (await acks.CurrentAsync(Ct)).ShouldBeNull("no row = not accepted");
            var first = await switches.AppendAsync(true, "owner:a@example.invalid", "first", Ct);
            var second = await switches.AppendAsync(false, "owner:a@example.invalid", "second", Ct);
            var ack = await acks.AppendAsync("owner:a@example.invalid", "read the table", Ct);

            (await switches.CurrentAsync(Ct))!.Id.ShouldBe(second.Id);
            (await switches.HistoryAsync(10, Ct)).Select(r => r.Id).ShouldBe([second.Id, first.Id]);
            (await acks.CurrentAsync(Ct))!.ShouldSatisfyAllConditions(a => a.Id.ShouldBe(ack.Id), a => a.Note.ShouldBe("read the table"));
            return 0;
        });
    }

    [Fact]
    public async Task The_stored_size_reads_and_the_target_resolution_runs_on_the_real_schema()
    {
        await WithAsync(async p =>
        {
            (await Store(p).StoredBytesAsync(Ct)).ShouldBeGreaterThanOrEqualTo(0);
            (await Store(p).FreeBytesAsync(Ct)).ShouldNotBeNull();
            await new BackfillTargets(p.GetRequiredService<IOptionUnderlyingSetting>(), p.GetRequiredService<NpgsqlDataSource>(), XDrishti.Application.Abstractions.OptionBackfill.OptionBackfillSettings.Off, TimeProvider.System).LoadAsync(Ct); // the query and the strike-step query parse and run
            return 0;
        });
    }

    [Fact]
    public async Task Every_outcome_word_fits_the_check_and_the_column()
    {
        BackfillOutcomes.All.Count.ShouldBe(12);
        BackfillOutcomes.All.Max(w => w.Length).ShouldBeLessThanOrEqualTo(16, "varchar(16)");
        var attempt = 0;
        foreach (var word in BackfillOutcomes.All)
        {
            await WithAsync(async p => await Store(p).AppendAsync(Attempt(word, ++attempt), [], Ct));
        }

        (await ScalarAsync<string>("SELECT string_agg(outcome, ',' ORDER BY attempt) FROM options.fetch_log")).ShouldBe(string.Join(',', BackfillOutcomes.All));
    }

    [Fact]
    public async Task The_resume_state_counts_rate_limits_as_nobodys_fault_and_answers_that_could_not_be_stored_as_a_limit_of_their_own()
    {
        var limited = From.AddDays(-30);
        var unstorable = From.AddDays(-60);
        await WithAsync(async p =>
        {
            var store = Store(p);
            foreach (var n in Enumerable.Range(1, 4))
            {
                await store.AppendAsync(Attempt("rate_limited", n, from: limited), [], Ct);
            }

            foreach (var n in Enumerable.Range(1, 3))
            {
                await store.AppendAsync(Attempt("store_failed", n, from: unstorable), [], Ct);
            }

            return 0;
        });

        var state = await new BackfillFetchLog(Factory.Services.GetRequiredService<NpgsqlDataSource>()).LoadStateAsync(Ct);

        state[new BackfillKey(Underlying, 'W', 1, 10, "CE", 5, limited)].ShouldSatisfyAllConditions(s => s.Failed.ShouldBe(0), s => s.IsGivenUp.ShouldBeFalse());
        state[new BackfillKey(Underlying, 'W', 1, 10, "CE", 5, unstorable)].ShouldSatisfyAllConditions(s => s.StoreFailures.ShouldBe(3), s => s.IsGivenUp.ShouldBeTrue(), s => s.IsDone.ShouldBeFalse());
    }

    [Fact]
    public async Task The_ranked_shares_are_frozen_by_the_first_write_and_a_later_ranking_cannot_move_the_plan()
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "DELETE FROM platform.app_settings WHERE key = 'options.backfill_stocks'");
        var list = new BackfillStockList(Factory.Services.GetRequiredService<NpgsqlDataSource>());
        var now = new DateTimeOffset(2026, 10, 12, 20, 0, 0, TimeSpan.FromMinutes(330));

        (await list.ReadAsync(Ct)).ShouldBeEmpty();
        (await list.WriteOnceAsync(["NSE:TCS", "NSE:RELIANCE", "NSE:INFY"], "service:options-backfill", now, Ct)).ShouldBeTrue();
        (await list.WriteOnceAsync(["NSE:HDFCBANK", "NSE:TCS"], "service:options-backfill", now, Ct)).ShouldBeFalse("the first call wins");

        (await list.ReadAsync(Ct)).ShouldBe(["NSE:TCS", "NSE:RELIANCE", "NSE:INFY"]);
        BackfillStockList.Parse("not json").ShouldBeEmpty();
        BackfillStockList.Parse("""["a", " "]""").ShouldBe(["a"]);
        await ExecuteAsync(connection, "DELETE FROM platform.app_settings WHERE key = 'options.backfill_stocks'");
    }
}
