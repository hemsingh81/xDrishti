using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Depth;
using XDrishti.Application.Abstractions.Ticks;
using XDrishti.Infrastructure.Depth;
using XDrishti.Infrastructure.Ticks;

namespace XDrishti.Api.IntegrationTests.Ticks;

/// <summary>
/// P7-16b on a real TimescaleDB: the hypertable and its compression settings, a synthetic day that compresses and reads back identically, the COPY writer (all or none), the constraints, the day's set (a copy of the depth set, never a depth write),
/// the end row of a day and the owner's switch rows. Tick rows are only ever read here by raw SQL of the test.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class TickRecordingDatabaseTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly DateOnly Day = new(2026, 11, 2);

    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 10, 0, 0, TimeSpan.FromMinutes(330));

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "TRUNCATE market.trade_ticks, market.tick_session_instruments, market.tick_sessions");
        await ExecuteAsync(connection, "TRUNCATE market.tick_recorder_switch RESTART IDENTITY");
    }

    private static TickStore Store(IServiceProvider p) => new(p.GetRequiredService<NpgsqlDataSource>());

    private async Task<T> WithAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        using var scope = Factory.Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    private static TradeTick Row(Guid instrument, DateTimeOffset ts, long seq, long volume, short flags = 0) =>
        new(ts, instrument, 1000 + seq, seq, (int)ts.ToUnixTimeSeconds(), 295_050 + (int)(seq % 7), (int)(seq % 300), volume, flags);

    [Fact]
    public async Task The_tick_table_is_a_one_day_hypertable_compressed_after_two_days_by_instrument_and_ts_rx_seq_with_no_retention()
    {
        (await ScalarAsync<string>("SELECT time_interval::text FROM timescaledb_information.dimensions WHERE hypertable_schema = 'market' AND hypertable_name = 'trade_ticks'")).ShouldBe("1 day");
        (await ScalarAsync<bool>("SELECT compression_enabled FROM timescaledb_information.hypertables WHERE hypertable_schema = 'market' AND hypertable_name = 'trade_ticks'")).ShouldBeTrue();
        (await ScalarAsync<string>("SELECT attname FROM timescaledb_information.compression_settings WHERE hypertable_schema = 'market' AND hypertable_name = 'trade_ticks' AND segmentby_column_index IS NOT NULL")).ShouldBe("instrument_id");
        (await ScalarAsync<string>("SELECT string_agg(attname, ',' ORDER BY orderby_column_index) FROM timescaledb_information.compression_settings WHERE hypertable_schema = 'market' AND hypertable_name = 'trade_ticks' AND orderby_column_index IS NOT NULL")).ShouldBe("ts,rx_seq");
        (await ScalarAsync<string>("SELECT (config ->> 'compress_after') FROM timescaledb_information.jobs WHERE proc_name = 'policy_compression' AND hypertable_name = 'trade_ticks'")).ShouldBe("2 days");
        (await ScalarAsync<long>("SELECT count(*) FROM timescaledb_information.jobs WHERE hypertable_name = 'trade_ticks' AND proc_name = 'policy_retention'")).ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM pg_indexes WHERE schemaname = 'market' AND indexname = 'ix_trade_ticks_instrument_ts'")).ShouldBe(1);
    }

    [Fact]
    public async Task A_synthetic_day_round_trips_through_copy_and_compression_identically()
    {
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();
        var rows = Enumerable.Range(1, 800).Select(i => Row(i % 2 == 0 ? a : b, T0.AddDays(-5).AddMilliseconds(i * 250), i, 1_000 + i, i == 1 || i == 2 ? TickFlags.Baseline : (short)0)).ToList();

        var written = await WithAsync(async p => await Store(p).AppendAsync(rows, Ct));
        const string Digest = "SELECT md5(string_agg(concat_ws('|', ts, instrument_id, rx_mono_ms, rx_seq, ltt_epoch, ltp_px, ltq, cum_vol, flags), ';' ORDER BY ts, rx_seq)) FROM market.trade_ticks";
        var before = await ScalarAsync<string>(Digest);
        (await ScalarAsync<long>("SELECT count(*) FROM (SELECT compress_chunk(c) FROM show_chunks('market.trade_ticks') c) x")).ShouldBeGreaterThan(0);
        var after = await ScalarAsync<string>(Digest);

        written.ShouldBe(800);
        after.ShouldBe(before);
        (await ScalarAsync<long>("SELECT sum(cum_vol)::bigint FROM market.trade_ticks")).ShouldBe(rows.Sum(r => r.CumVol));
        (await ScalarAsync<long>("SELECT count(*) FROM timescaledb_information.chunks WHERE hypertable_name = 'trade_ticks' AND is_compressed")).ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task A_bad_row_writes_none_of_its_batch()
    {
        var id = Guid.CreateVersion7();

        await Should.ThrowAsync<PostgresException>(() => WithAsync(async p => await Store(p).AppendAsync([Row(id, T0, 1, 10), Row(id, T0.AddSeconds(1), 2, 11) with { LtpPx = 0 }], Ct)));

        (await ScalarAsync<long>("SELECT count(*) FROM market.trade_ticks")).ShouldBe(0);
    }

    [Theory]
    [InlineData("ltp_px", "0")]
    [InlineData("ltq", "-1")]
    [InlineData("cum_vol", "-1")]
    [InlineData("flags", "16")]
    [InlineData("flags", "-1")]
    public async Task The_database_enforces_the_shape_of_a_tick(string column, string value)
    {
        await using var connection = await OpenAsync();
        var values = new Dictionary<string, string> { ["ltp_px"] = "100", ["ltq"] = "1", ["cum_vol"] = "1", ["flags"] = "0" };
        values[column] = value;

        var failure = await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connection, $"INSERT INTO market.trade_ticks (ts, instrument_id, rx_mono_ms, rx_seq, ltt_epoch, ltp_px, ltq, cum_vol, flags) VALUES (now(), gen_random_uuid(), 1, 1, 1, {values["ltp_px"]}, {values["ltq"]}, {values["cum_vol"]}, {values["flags"]})"));

        failure.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task The_set_and_session_tables_refuse_unknown_words()
    {
        await using var connection = await OpenAsync();

        foreach (var sql in new[]
        {
            "INSERT INTO market.tick_session_instruments (session_date, instrument_id, source, reason, rank) VALUES ('2026-11-02', gen_random_uuid(), 'guess', 'armed', 1)",
            "INSERT INTO market.tick_session_instruments (session_date, instrument_id, source, reason, rank) VALUES ('2026-11-02', gen_random_uuid(), 'built', 'liquid', 1)",
            "INSERT INTO market.tick_sessions (session_date, started_at, ended_at, end_reason, instruments, packets, rows, unchanged, dropped_rows, gap_seconds) VALUES ('2026-11-02', now(), now(), 'sleepy', 1, 1, 1, 0, 0, 0)",
        })
        {
            (await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connection, sql))).SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        }
    }

    [Fact]
    public async Task The_depth_set_is_read_as_a_copy_the_tick_set_is_stored_once_and_no_depth_row_is_written_so_depth_recovery_finds_no_crashed_day()
    {
        var a = await SeedAsync("TICKA");
        var b = await SeedAsync("TICKB");
        var securityA = int.Parse(await ScalarAsync<string>("SELECT broker_id FROM market.instrument_broker_ids WHERE instrument_id = @i", ("i", a)), System.Globalization.CultureInfo.InvariantCulture);
        var depthDay = Day.AddDays(1);
        await WithAsync(async p => { await new DepthStore(p.GetRequiredService<NpgsqlDataSource>()).SaveSetAsync(depthDay, [new DepthSetEntry(a, securityA, "NSE:TICKA", "armed", 1)], Ct); return 0; });

        var copied = await WithAsync(async p => await Store(p).LoadDepthSetAsync(depthDay, Ct));
        (await WithAsync(async p => await Store(p).LoadDepthSetAsync(Day, Ct))).ShouldBeEmpty();
        var depthRowsBefore = await ScalarAsync<long>("SELECT count(*) FROM market.depth_session_instruments");
        await WithAsync(async p => { await Store(p).SaveSetAsync(Day, [new TickSetEntry(a, securityA, "NSE:TICKA", "built", "armed", 1), new TickSetEntry(b, 1, "NSE:TICKB", "built", "turnover", 2)], Ct); return 0; });
        await WithAsync(async p => { await Store(p).SaveSetAsync(Day, [new TickSetEntry(a, 1, "x", "depth_set", "pinned", 9)], Ct); return 0; });
        var loaded = await WithAsync(async p => await Store(p).LoadSetAsync(Day, Ct));
        var depthUnfinished = await WithAsync(async p => await new DepthStore(p.GetRequiredService<NpgsqlDataSource>()).FindUnfinishedDaysAsync(Ct));

        copied.ShouldHaveSingleItem().ShouldBe(new TickSetEntry(a, securityA, "NSE:TICKA", "depth_set", "armed", 1));
        loaded.Select(e => (e.Symbol, e.Source, e.Reason, e.Rank)).ShouldBe([("NSE:TICKA", "built", "armed", 1), ("NSE:TICKB", "built", "turnover", 2)]);
        (await ScalarAsync<long>("SELECT count(*) FROM market.depth_session_instruments")).ShouldBe(depthRowsBefore, "the tick store never writes a depth table");
        depthUnfinished.ShouldNotContain(Day, "a day with ticks only is not a depth day, so depth recovery writes no crashed row for it");
        (await ScalarAsync<long>("SELECT count(*) FROM market.depth_sessions WHERE session_date = @d", ("d", Day))).ShouldBe(0);
    }

    [Fact]
    public async Task A_day_with_a_tick_set_and_no_end_row_is_unfinished_until_its_row_is_written_and_the_first_end_row_wins()
    {
        var a = await SeedAsync("TICKC");
        await WithAsync(async p => { await Store(p).SaveSetAsync(Day, [new TickSetEntry(a, 1, "NSE:TICKC", "built", "armed", 1)], Ct); return 0; });
        (await WithAsync(async p => await Store(p).FindUnfinishedDaysAsync(Ct))).ShouldBe([Day]);

        var first = await WithAsync(async p => await Store(p).RecordSessionAsync(new TickSessionRecord(Day, T0, T0.AddHours(5), "window_end", 1, 9_000, 1_200, 7_000, 3, 42, 4096, null), Ct));
        var second = await WithAsync(async p => await Store(p).RecordSessionAsync(new TickSessionRecord(Day, T0, T0.AddHours(6), "crashed", 1, 0, 0, 0, 0, 0, null, "x"), Ct));

        (first, second).ShouldBe((true, false));
        (await WithAsync(async p => await Store(p).FindUnfinishedDaysAsync(Ct))).ShouldBeEmpty();
        (await ScalarAsync<string>("SELECT end_reason || ':' || rows || ':' || unchanged || ':' || gap_seconds FROM market.tick_sessions WHERE session_date = @d", ("d", Day))).ShouldBe("window_end:1200:7000:42");
    }

    [Fact]
    public async Task The_switch_has_no_row_until_the_owner_acts_and_the_database_refuses_a_non_owner_or_a_blank_reason()
    {
        (await WithAsync(async p => await p.GetRequiredService<ITickSwitchStore>().CurrentAsync(Ct))).ShouldBeNull();

        var on = await WithAsync(async p => await p.GetRequiredService<ITickSwitchStore>().AppendAsync(true, "owner:me@example.invalid", "start", Ct));
        var off = await WithAsync(async p => await p.GetRequiredService<ITickSwitchStore>().AppendAsync(false, "owner:me@example.invalid", "stop", Ct));

        off.Id.ShouldBeGreaterThan(on.Id);
        (await WithAsync(async p => await p.GetRequiredService<ITickSwitchStore>().CurrentAsync(Ct)))!.Enabled.ShouldBeFalse();
        (await WithAsync(async p => await p.GetRequiredService<ITickSwitchStore>().HistoryAsync(10, Ct))).Select(r => r.Id).ShouldBe([off.Id, on.Id]);
        await Should.ThrowAsync<PostgresException>(() => WithAsync(async p => await p.GetRequiredService<ITickSwitchStore>().AppendAsync(true, "service:feed", "x", Ct)));
        await Should.ThrowAsync<PostgresException>(() => WithAsync(async p => await p.GetRequiredService<ITickSwitchStore>().AppendAsync(true, "owner:me@example.invalid", "   ", Ct)));
    }

    [Fact]
    public async Task The_stored_size_and_the_free_space_are_readable()
    {
        var (stored, free) = await WithAsync(async p => (await Store(p).StoredBytesAsync(Ct), await Store(p).FreeBytesAsync(Ct)));

        stored.ShouldBeGreaterThanOrEqualTo(0);
        free.ShouldNotBeNull().ShouldBeGreaterThan(0);
    }
}
