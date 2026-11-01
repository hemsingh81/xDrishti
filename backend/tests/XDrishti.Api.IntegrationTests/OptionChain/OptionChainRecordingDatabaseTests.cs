using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.OptionChain;
using XDrishti.Application.Abstractions.Options;
using XDrishti.Infrastructure.OptionChain;

namespace XDrishti.Api.IntegrationTests.OptionChain;

/// <summary>
/// P14-02 on a real TimescaleDB (criteria 4, 5, 9): the hypertable and its compression settings, a synthetic day that compresses and reads back identically, the idempotent re-insert of a slot, a failed request that has a request row and no snapshot,
/// the constraints, the end row of a day, the owner's switch rows and the underlyings resolved from the master list. Snapshot rows are only ever read here by raw SQL of the test.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class OptionChainRecordingDatabaseTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly Guid Underlying = Guid.Parse("00000000-0000-0000-0000-0000000000c1");

    private static readonly DateTimeOffset Slot = new(2026, 10, 12, 9, 20, 0, TimeSpan.FromMinutes(330));

    private static readonly DateOnly Expiry = new(2026, 10, 20);

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "TRUNCATE options.chain_snapshots, options.chain_requests, options.chain_sessions");
        await ExecuteAsync(connection, "TRUNCATE options.chain_recorder_switch RESTART IDENTITY");
        await ExecuteAsync(connection, "DELETE FROM platform.app_settings WHERE key = 'options.underlyings'");
    }

    private async Task<T> WithAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        using var scope = Factory.Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    private static OptionChainStore Store(IServiceProvider p) => new(p.GetRequiredService<NpgsqlDataSource>());

    private static ChainTargets Targets(IServiceProvider p) => new(p.GetRequiredService<IOptionUnderlyingSetting>(), p.GetRequiredService<NpgsqlDataSource>());

    private static ChainSnapshotRow Row(int strike, string side, DateTimeOffset? ts = null, Guid? underlying = null) =>
        new(ts ?? Slot, underlying ?? Underlying, Expiry, strike, side, (ts ?? Slot).AddSeconds(4), 42_000 + strike, 2_564_280, 13_400, 14_699, 24_485, 13_355, 1625, 13_400, 1365, 3_786_445, 402_220, 117_567_970, 31_931_705, 9.789f, 0.53871f, 0.00132f, -15.1539f, 12.18593f, 0);

    private static ChainRequestRecord Request(string outcome = "ok", int rows = 0, DateTimeOffset? slot = null, string kind = "chain", DateOnly? expiry = null) =>
        new(slot ?? Slot, kind, Underlying, kind == "chain" ? expiry ?? Expiry : null, outcome, outcome == "ok" ? 200 : 503, outcome == "ok" ? null : "DH-908", rows, 120, (slot ?? Slot).AddSeconds(4));

    [Fact]
    public async Task The_snapshot_table_is_a_seven_day_hypertable_compressed_after_seven_days_by_underlying_and_expiry_with_no_retention()
    {
        (await ScalarAsync<string>("SELECT time_interval::text FROM timescaledb_information.dimensions WHERE hypertable_schema = 'options' AND hypertable_name = 'chain_snapshots'")).ShouldBe("7 days");
        (await ScalarAsync<bool>("SELECT compression_enabled FROM timescaledb_information.hypertables WHERE hypertable_schema = 'options' AND hypertable_name = 'chain_snapshots'")).ShouldBeTrue();
        (await ScalarAsync<string>("SELECT string_agg(attname, ',' ORDER BY segmentby_column_index) FROM timescaledb_information.compression_settings WHERE hypertable_schema = 'options' AND hypertable_name = 'chain_snapshots' AND segmentby_column_index IS NOT NULL")).ShouldBe("underlying_id,expiry");
        (await ScalarAsync<string>("SELECT string_agg(attname, ',' ORDER BY orderby_column_index) FROM timescaledb_information.compression_settings WHERE hypertable_schema = 'options' AND hypertable_name = 'chain_snapshots' AND orderby_column_index IS NOT NULL")).ShouldBe("ts,strike_px,side");
        (await ScalarAsync<string>("SELECT (config ->> 'compress_after') FROM timescaledb_information.jobs WHERE proc_name = 'policy_compression' AND hypertable_name = 'chain_snapshots'")).ShouldBe("7 days");
        (await ScalarAsync<long>("SELECT count(*) FROM timescaledb_information.jobs WHERE hypertable_name = 'chain_snapshots' AND proc_name = 'policy_retention'")).ShouldBe(0);
        (await ScalarAsync<string>("SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = 'pk_chain_snapshots'")).ShouldBe("PRIMARY KEY (ts, underlying_id, expiry, strike_px, side)");
    }

    [Fact]
    public async Task A_synthetic_slot_round_trips_through_copy_and_compression_identically()
    {
        var old = Slot.AddDays(-20);
        var rows = Enumerable.Range(0, 40).SelectMany(i => new[] { Row(2_500_000 + (i * 5_000), "CE", old), Row(2_500_000 + (i * 5_000), "PE", old) }).ToList();

        var written = await WithAsync(async p => await Store(p).AppendAsync(Request("ok", rows.Count, old), rows, Ct));
        const string Digest = "SELECT md5(string_agg(concat_ws('|', ts, underlying_id, expiry, strike_px, side, rx_at, security_id, underlying_px, last_px, avg_px, prev_close_px, bid_px, bid_qty, ask_px, ask_qty, oi, prev_oi, volume, prev_volume, iv, delta, gamma, theta, vega, flags), ';' ORDER BY strike_px, side)) FROM options.chain_snapshots";
        var before = await ScalarAsync<string>(Digest);
        (await ScalarAsync<long>("SELECT count(*) FROM (SELECT compress_chunk(c) FROM show_chunks('options.chain_snapshots') c) x")).ShouldBeGreaterThan(0);
        var after = await ScalarAsync<string>(Digest);

        written.ShouldBe(80);
        after.ShouldBe(before);
        (await ScalarAsync<long>("SELECT count(*) FROM timescaledb_information.chunks WHERE hypertable_name = 'chain_snapshots' AND is_compressed")).ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task A_re_run_of_a_slot_inserts_no_snapshot_row_and_writes_a_request_row_that_says_so()
    {
        var rows = new[] { Row(2_565_000, "CE"), Row(2_565_000, "PE") };

        var first = await WithAsync(async p => await Store(p).AppendAsync(Request(), rows, Ct));
        var second = await WithAsync(async p => await Store(p).AppendAsync(Request(), rows, Ct));

        (first, second).ShouldBe((2, 0));
        (await ScalarAsync<long>("SELECT count(*) FROM options.chain_snapshots")).ShouldBe(2);
        (await ScalarAsync<string>("SELECT string_agg(rows::text, ',' ORDER BY id) FROM options.chain_requests")).ShouldBe("2,0");
    }

    [Fact]
    public async Task A_missing_value_is_stored_as_null_never_as_a_zero()
    {
        var row = Row(2_565_000, "CE") with { LastPx = null, BidPx = null, BidQty = null, AskPx = null, AskQty = null, Iv = null, Oi = null, Flags = 7 };

        await WithAsync(async p => await Store(p).AppendAsync(Request(), [row], Ct));

        (await ScalarAsync<long>("SELECT count(*) FROM options.chain_snapshots WHERE last_px IS NULL AND bid_px IS NULL AND bid_qty IS NULL AND ask_px IS NULL AND iv IS NULL AND oi IS NULL AND flags = 7")).ShouldBe(1);
    }

    [Fact]
    public async Task A_failed_request_has_a_request_row_and_no_snapshot_row()
    {
        await WithAsync(async p => await Store(p).AppendAsync(Request("server_error"), [], Ct));

        (await ScalarAsync<long>("SELECT count(*) FROM options.chain_snapshots")).ShouldBe(0);
        (await ScalarAsync<string>("SELECT outcome || ':' || http_status || ':' || dhan_code || ':' || rows FROM options.chain_requests")).ShouldBe("server_error:503:DH-908:0");
    }

    [Fact]
    public async Task A_bad_row_writes_none_of_its_request()
    {
        var bad = Row(2_565_100, "XX");

        await Should.ThrowAsync<PostgresException>(() => WithAsync(async p => await Store(p).AppendAsync(Request(), [Row(2_565_000, "CE"), bad], Ct)));

        (await ScalarAsync<long>("SELECT count(*) FROM options.chain_snapshots")).ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM options.chain_requests")).ShouldBe(0);
    }

    [Theory]
    [InlineData("side", "'XX'")]
    [InlineData("strike_px", "0")]
    [InlineData("underlying_px", "0")]
    [InlineData("flags", "32")]
    [InlineData("flags", "-1")]
    [InlineData("bid_px", "0")]
    [InlineData("last_px", "0")]
    [InlineData("oi", "-1")]
    [InlineData("bid_qty", "-1")]
    public async Task The_database_enforces_the_shape_of_a_snapshot(string column, string value)
    {
        await using var connection = await OpenAsync();
        var values = new Dictionary<string, string> { ["side"] = "'CE'", ["strike_px"] = "100", ["underlying_px"] = "100", ["flags"] = "0", ["bid_px"] = "100", ["last_px"] = "100", ["oi"] = "1", ["bid_qty"] = "1" };
        values[column] = value;

        var failure = await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connection, $"INSERT INTO options.chain_snapshots (ts, underlying_id, expiry, strike_px, side, rx_at, security_id, underlying_px, last_px, bid_px, bid_qty, oi, flags) VALUES (now(), gen_random_uuid(), '2026-10-20', {values["strike_px"]}, {values["side"]}, now(), 1, {values["underlying_px"]}, {values["last_px"]}, {values["bid_px"]}, {values["bid_qty"]}, {values["oi"]}, {values["flags"]})"));

        failure.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task The_request_and_session_tables_refuse_unknown_words_and_a_chain_without_an_expiry()
    {
        await using var connection = await OpenAsync();

        foreach (var sql in new[]
        {
            "INSERT INTO options.chain_requests (slot_ts, kind, underlying_id, expiry, outcome, rows, latency_ms, requested_at) VALUES (now(), 'chain', gen_random_uuid(), '2026-10-20', 'sleepy', 0, 0, now())",
            "INSERT INTO options.chain_requests (slot_ts, kind, underlying_id, expiry, outcome, rows, latency_ms, requested_at) VALUES (now(), 'chain', gen_random_uuid(), NULL, 'ok', 0, 0, now())",
            "INSERT INTO options.chain_requests (slot_ts, kind, underlying_id, expiry, outcome, rows, latency_ms, requested_at) VALUES (now(), 'expiries', gen_random_uuid(), '2026-10-20', 'ok', 0, 0, now())",
            "INSERT INTO options.chain_sessions (session_date, started_at, ended_at, end_reason, slots, requests, ok, gaps, rows) VALUES ('2026-11-02', now(), now(), 'sleepy', 1, 1, 1, 0, 1)",
        })
        {
            (await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connection, sql))).SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        }
    }

    [Theory]
    [InlineData("ok")]
    [InlineData("rate_limited")]
    [InlineData("timeout")]
    [InlineData("server_error")]
    [InlineData("no_data")]
    [InlineData("bad_body")]
    [InlineData("token_refused")]
    [InlineData("not_subscribed")]
    [InlineData("slot_overrun")]
    [InlineData("window_end")]
    [InlineData("disk_stop")]
    public async Task Every_outcome_word_of_the_spec_is_accepted(string outcome)
    {
        await WithAsync(async p => await Store(p).AppendAsync(Request(outcome), [], Ct));

        (await ScalarAsync<long>("SELECT count(*) FROM options.chain_requests WHERE outcome = @o", ("o", outcome))).ShouldBe(1);
    }

    [Fact]
    public async Task A_day_with_a_request_row_and_no_end_row_is_unfinished_by_its_india_date_and_the_first_end_row_wins()
    {
        var late = new DateTimeOffset(2026, 10, 9, 23, 55, 0, TimeSpan.FromMinutes(330)); // 18:25 UTC: the India date is the 9th
        var day = new DateOnly(2026, 10, 9);
        await WithAsync(async p => await Store(p).AppendAsync(Request("ok", 0, late), [], Ct));
        (await WithAsync(async p => await Store(p).FindUnfinishedDaysAsync(Ct))).ShouldBe([day]);

        var first = await WithAsync(async p => await Store(p).RecordSessionAsync(new ChainSessionRecord(day, late, late.AddHours(1), "window_end", 75, 515, 500, 15, 58_620, 4096, null), Ct));
        var second = await WithAsync(async p => await Store(p).RecordSessionAsync(new ChainSessionRecord(day, late, late.AddHours(2), "crashed", 0, 0, 0, 0, 0, null, "x"), Ct));

        (first, second).ShouldBe((true, false));
        (await WithAsync(async p => await Store(p).FindUnfinishedDaysAsync(Ct))).ShouldBeEmpty();
        (await ScalarAsync<string>("SELECT end_reason || ':' || rows || ':' || gaps FROM options.chain_sessions WHERE session_date = @d", ("d", day))).ShouldBe("window_end:58620:15");
    }

    [Fact]
    public async Task The_switch_has_no_row_until_the_owner_acts_and_the_database_refuses_a_non_owner_or_a_blank_reason()
    {
        (await WithAsync(async p => await p.GetRequiredService<IOptionChainSwitchStore>().CurrentAsync(Ct))).ShouldBeNull();

        var on = await WithAsync(async p => await p.GetRequiredService<IOptionChainSwitchStore>().AppendAsync(true, "owner:me@example.invalid", "start", Ct));
        var off = await WithAsync(async p => await p.GetRequiredService<IOptionChainSwitchStore>().AppendAsync(false, "owner:me@example.invalid", "stop", Ct));

        off.Id.ShouldBeGreaterThan(on.Id);
        (await WithAsync(async p => await p.GetRequiredService<IOptionChainSwitchStore>().CurrentAsync(Ct)))!.Enabled.ShouldBeFalse();
        (await WithAsync(async p => await p.GetRequiredService<IOptionChainSwitchStore>().HistoryAsync(10, Ct))).Select(r => r.Id).ShouldBe([off.Id, on.Id]);
        await Should.ThrowAsync<PostgresException>(() => WithAsync(async p => await p.GetRequiredService<IOptionChainSwitchStore>().AppendAsync(true, "service:feed", "x", Ct)));
        await Should.ThrowAsync<PostgresException>(() => WithAsync(async p => await p.GetRequiredService<IOptionChainSwitchStore>().AppendAsync(true, "owner:me@example.invalid", "   ", Ct)));
    }

    [Fact]
    public async Task The_underlyings_are_the_setting_resolved_to_dhan_ids_an_index_by_its_index_id_a_share_by_its_equity_id()
    {
        var share = await SeedAsync("CHAINSH");
        var index = await SeedAsync("CHAINIX", type: "index", dhanId: false);
        var noId = await SeedAsync("CHAINNO", dhanId: false);
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "INSERT INTO market.instrument_broker_ids (instrument_id, broker, broker_id, segment) VALUES (@id, 'dhan', '9013', 'IDX_I')", ("id", index));
        }

        var shareId = await ScalarAsync<string>("SELECT broker_id FROM market.instrument_broker_ids WHERE instrument_id = @i", ("i", share));
        await WithAsync(async p => await p.GetRequiredService<IOptionUnderlyingSetting>().FixDefaultAsync("[\"NSE:CHAINIX\",\"NSE:CHAINSH\",\"NSE:CHAINNO\",\"NSE:ABSENT\"]", "test", Now, Ct));

        var targets = await WithAsync(async p => await Targets(p).LoadAsync(Ct));

        targets.Select(t => (t.Symbol, t.Scrip, t.Segment, t.IsIndex)).ShouldBe([("NSE:CHAINIX", 9013L, "IDX_I", true), ("NSE:CHAINSH", long.Parse(shareId, System.Globalization.CultureInfo.InvariantCulture), "NSE_EQ", false)]);
        targets.Select(t => t.InstrumentId).ShouldBe([index, share]);
        targets.Select(t => t.InstrumentId).ShouldNotContain(noId);
    }

    [Fact]
    public async Task With_no_setting_there_are_no_underlyings()
    {
        (await WithAsync(async p => await Targets(p).LoadAsync(Ct))).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_stored_size_and_the_free_space_are_readable()
    {
        var (stored, free) = await WithAsync(async p => (await Store(p).StoredBytesAsync(Ct), await Store(p).FreeBytesAsync(Ct)));

        stored.ShouldBeGreaterThanOrEqualTo(0);
        free.ShouldNotBeNull().ShouldBeGreaterThan(0);
    }
}
