using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Depth;
using XDrishti.Infrastructure.Depth;

namespace XDrishti.Api.IntegrationTests.Depth;

/// <summary>
/// P7-16 on a real TimescaleDB: the set and the end row of a day, the liquidity ranking over daily bars, the owner's switch rows, the COPY writer (a batch is all or none) and the hypertable's settings. Depth rows are only ever read here by raw SQL of the test.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class DepthRecordingDatabaseTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly DateOnly Day = new(2026, 10, 12);

    private static readonly DateTimeOffset T0 = new(2026, 10, 12, 10, 0, 0, TimeSpan.FromMinutes(330));

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "TRUNCATE market.depth_snapshots, market.depth_session_instruments, market.depth_sessions");
        await ExecuteAsync(connection, "TRUNCATE market.depth_recorder_switch RESTART IDENTITY");
    }

    private static DepthStore Store(IServiceProvider p) => new(p.GetRequiredService<NpgsqlDataSource>());

    private async Task<T> WithAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        using var scope = Factory.Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    private static int[] Level(int start, int step) => [.. Enumerable.Range(0, 20).Select(i => start + (step * i))];

    private static DepthSnapshot Row(Guid instrument, DateTimeOffset ts, int seq = 1, int bidStart = 10000) =>
        new(ts, instrument, seq, 1500 + seq, 0, Level(bidStart, -5), Level(100, 1), Level(3, 0), Level(bidStart + 5, 5), Level(200, 2), Level(4, 0));

    [Fact]
    public async Task The_days_set_is_stored_once_with_symbol_and_security_id_and_a_stored_row_is_left_alone()
    {
        var a = await SeedAsync("DEPTHA");
        var b = await SeedAsync("DEPTHB");
        var securityA = await ScalarAsync<string>("SELECT broker_id FROM market.instrument_broker_ids WHERE instrument_id = @i", ("i", a));
        var set = new List<DepthSetEntry> { new(a, int.Parse(securityA, System.Globalization.CultureInfo.InvariantCulture), "NSE:DEPTHA", "armed", 1), new(b, 1, "NSE:DEPTHB", "turnover", 2) };

        await WithAsync(async p => { await Store(p).SaveSetAsync(Day, set, Ct); return 0; });
        await WithAsync(async p => { await Store(p).SaveSetAsync(Day, [new DepthSetEntry(a, 1, "x", "pinned", 9)], Ct); return 0; });
        var loaded = await WithAsync(async p => await Store(p).LoadSetAsync(Day, Ct));

        loaded.Select(e => (e.Symbol, e.Reason, e.Rank)).ShouldBe([("NSE:DEPTHA", "armed", 1), ("NSE:DEPTHB", "turnover", 2)]);
        loaded[0].SecurityId.ShouldBe(set[0].SecurityId);
        (await WithAsync(async p => await Store(p).LoadSetAsync(Day.AddDays(1), Ct))).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_day_with_a_set_and_no_end_row_is_unfinished_until_its_row_is_written_and_the_first_end_row_wins()
    {
        var a = await SeedAsync("DEPTHC");
        await WithAsync(async p => { await Store(p).SaveSetAsync(Day, [new DepthSetEntry(a, 1, "NSE:DEPTHC", "armed", 1)], Ct); return 0; });
        (await WithAsync(async p => await Store(p).FindUnfinishedDaysAsync(Ct))).ShouldBe([Day]);

        var first = await WithAsync(async p => await Store(p).RecordSessionAsync(new DepthSessionRecord(Day, T0, T0.AddHours(5), "window_end", 1, 100, 500, 0, 3, 12345, "note"), Ct));
        var second = await WithAsync(async p => await Store(p).RecordSessionAsync(new DepthSessionRecord(Day, T0, T0.AddHours(6), "crashed", 1, 0, 0, 0, 0, null, null), Ct));

        (first, second).ShouldBe((true, false));
        (await WithAsync(async p => await Store(p).FindUnfinishedDaysAsync(Ct))).ShouldBeEmpty();
        (await ScalarAsync<string>("SELECT end_reason FROM market.depth_sessions WHERE session_date = @d", ("d", Day))).ShouldBe("window_end");
    }

    [Fact]
    public async Task The_liquidity_ranking_is_the_mean_of_close_times_volume_of_the_last_twenty_daily_bars_before_the_day_of_tracked_live_shares_only()
    {
        var rich = await SeedAsync("RICHD");
        var poor = await SeedAsync("POORD");
        var tie1 = await SeedAsync("TIEB");
        var tie2 = await SeedAsync("TIEA");
        var untracked = await SeedAsync("UNTRK");
        var retired = await SeedAsync("RETRD", retired: true);
        var index = await SeedAsync("IDXD", type: "index");
        var recentOnly = await SeedAsync("RECENT");
        await using (var connection = await OpenAsync())
        {
            foreach (var (id, volume) in new[] { (rich, 1_000_000L), (poor, 10L), (tie1, 500L), (tie2, 500L), (retired, 9_000_000L), (index, 9_000_000L), (untracked, 9_000_000L) })
            {
                foreach (var offset in Enumerable.Range(1, 25))
                {
                    await Bar(connection, id, Day.AddDays(-offset), 100, volume);
                }
            }

            // twenty-five old bars of volume 1, then 20 big recent ones: only the last 20 count
            foreach (var offset in Enumerable.Range(1, 40))
            {
                await Bar(connection, recentOnly, Day.AddDays(-offset), 100, offset <= 20 ? 100_000L : 1L);
            }

            await Bar(connection, poor, Day, 100, 99_999_999L); // the day itself is not "before"
            foreach (var id in new[] { rich, poor, tie1, tie2, retired, index, recentOnly })
            {
                await ExecuteAsync(connection, "INSERT INTO market.tracked_instruments (instrument_id, active, active_since, tracked_by) VALUES (@i, true, now(), 'probe')", ("i", id));
            }
        }

        var ranked = await WithAsync(async p => await new DepthUniverse(p.GetRequiredService<NpgsqlDataSource>()).MostLiquidAsync(Day, 10, Ct));

        ranked.Select(r => r.Security.Symbol).ShouldBe(["NSE:RICHD", "NSE:RECENT", "NSE:TIEA", "NSE:TIEB", "NSE:POORD"]);
        ranked[0].MeanTurnover.ShouldBe(100d * 1_000_000d);
        ranked[1].MeanTurnover.ShouldBe(100d * 100_000d);
        (await WithAsync(async p => await new DepthUniverse(p.GetRequiredService<NpgsqlDataSource>()).MostLiquidAsync(Day, 2, Ct))).Count.ShouldBe(2);
    }

    private static Task Bar(NpgsqlConnection connection, Guid id, DateOnly day, double close, long volume) =>
        ExecuteAsync(
            connection,
            "INSERT INTO market.bars_agg (instrument_id, timeframe, ts, open, high, low, close, volume, bars, complete) VALUES (@id, '1D', @ts, @c, @c, @c, @c, @v, 375, true)",
            ("id", id), ("ts", new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.FromMinutes(330)).ToUniversalTime()), ("c", close), ("v", volume));

    [Fact]
    public async Task The_switch_has_no_row_until_the_owner_acts_then_rows_pile_up_newest_first_and_the_database_refuses_a_non_owner_or_a_blank_reason()
    {
        var store = await WithAsync(p => Task.FromResult(p.GetRequiredService<IDepthSwitchStore>()));
        (await WithAsync(async p => await p.GetRequiredService<IDepthSwitchStore>().CurrentAsync(Ct))).ShouldBeNull();

        var on = await WithAsync(async p => await p.GetRequiredService<IDepthSwitchStore>().AppendAsync(true, "owner:me@example.invalid", "start", Ct));
        var off = await WithAsync(async p => await p.GetRequiredService<IDepthSwitchStore>().AppendAsync(false, "owner:me@example.invalid", "stop", Ct));

        store.ShouldNotBeNull();
        off.Id.ShouldBeGreaterThan(on.Id);
        off.ChangedAt.ShouldBeGreaterThan(DateTimeOffset.UnixEpoch);
        (await WithAsync(async p => await p.GetRequiredService<IDepthSwitchStore>().CurrentAsync(Ct)))!.Enabled.ShouldBeFalse();
        (await WithAsync(async p => await p.GetRequiredService<IDepthSwitchStore>().HistoryAsync(10, Ct))).Select(r => r.Id).ShouldBe([off.Id, on.Id]);
        await Should.ThrowAsync<PostgresException>(() => WithAsync(async p => await p.GetRequiredService<IDepthSwitchStore>().AppendAsync(true, "service:feed", "x", Ct)));
        await Should.ThrowAsync<PostgresException>(() => WithAsync(async p => await p.GetRequiredService<IDepthSwitchStore>().AppendAsync(true, "owner:", "x", Ct)));
        await Should.ThrowAsync<PostgresException>(() => WithAsync(async p => await p.GetRequiredService<IDepthSwitchStore>().AppendAsync(true, "owner:me@example.invalid", "   ", Ct)));
    }

    [Fact]
    public async Task A_batch_is_written_with_copy_and_reads_back_identically_and_a_bad_row_writes_none_of_the_batch()
    {
        var id = Guid.CreateVersion7();
        var good = new[] { Row(id, T0, 1), Row(id, T0.AddSeconds(1), 2, bidStart: 10100) };

        var written = await WithAsync(async p => await Store(p).AppendAsync(good, Ct));
        var bad = good[0] with { Ts = T0.AddSeconds(2), BidPx = Level(1, 1)[..19] };
        await Should.ThrowAsync<PostgresException>(() => WithAsync(async p => await Store(p).AppendAsync([Row(id, T0.AddSeconds(3), 3), bad], Ct)));

        written.ShouldBe(2);
        (await ScalarAsync<long>("SELECT count(*) FROM market.depth_snapshots")).ShouldBe(2);
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand("SELECT ts, seq, rx_mono_ms, flags, bid_px, bid_qty, bid_ord, ask_px, ask_qty, ask_ord FROM market.depth_snapshots ORDER BY ts", connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        foreach (var expected in good)
        {
            (await reader.ReadAsync(Ct)).ShouldBeTrue();
            reader.GetDateTime(0).ShouldBe(expected.Ts.UtcDateTime);
            reader.GetInt32(1).ShouldBe(expected.Seq);
            reader.GetInt64(2).ShouldBe(expected.RxMonoMs);
            reader.GetInt16(3).ShouldBe(expected.Flags);
            reader.GetFieldValue<int[]>(4).ShouldBe(expected.BidPx);
            reader.GetFieldValue<int[]>(5).ShouldBe(expected.BidQty);
            reader.GetFieldValue<int[]>(6).ShouldBe(expected.BidOrd);
            reader.GetFieldValue<int[]>(7).ShouldBe(expected.AskPx);
            reader.GetFieldValue<int[]>(8).ShouldBe(expected.AskQty);
            reader.GetFieldValue<int[]>(9).ShouldBe(expected.AskOrd);
        }
    }

    [Fact]
    public async Task The_stored_size_and_the_free_space_are_readable()
    {
        var store = await WithAsync(async p => (await Store(p).StoredBytesAsync(Ct), await Store(p).FreeBytesAsync(Ct)));

        store.Item1.ShouldBeGreaterThanOrEqualTo(0);
        store.Item2.ShouldNotBeNull().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task The_snapshot_table_is_a_one_day_hypertable_compressed_after_two_days_with_no_retention_and_a_synthetic_day_compresses_and_reads_back_identically()
    {
        (await ScalarAsync<string>("SELECT (SELECT time_interval::text FROM timescaledb_information.dimensions WHERE hypertable_schema = 'market' AND hypertable_name = 'depth_snapshots')")).ShouldBe("1 day");
        (await ScalarAsync<bool>("SELECT compression_enabled FROM timescaledb_information.hypertables WHERE hypertable_schema = 'market' AND hypertable_name = 'depth_snapshots'")).ShouldBeTrue();
        (await ScalarAsync<string>("SELECT segmentby_column_index::text || ':' || attname FROM timescaledb_information.compression_settings WHERE hypertable_schema = 'market' AND hypertable_name = 'depth_snapshots' AND segmentby_column_index IS NOT NULL")).ShouldBe("1:instrument_id");
        (await ScalarAsync<string>("SELECT attname FROM timescaledb_information.compression_settings WHERE hypertable_schema = 'market' AND hypertable_name = 'depth_snapshots' AND orderby_column_index IS NOT NULL")).ShouldBe("ts");
        (await ScalarAsync<string>("SELECT (config ->> 'compress_after') FROM timescaledb_information.jobs WHERE proc_name = 'policy_compression' AND hypertable_name = 'depth_snapshots'")).ShouldBe("2 days");
        (await ScalarAsync<long>("SELECT count(*) FROM timescaledb_information.jobs WHERE hypertable_name = 'depth_snapshots' AND proc_name = 'policy_retention'")).ShouldBe(0);

        var id = Guid.CreateVersion7();
        var rows = Enumerable.Range(0, 600).Select(i => Row(id, T0.AddDays(-5).AddSeconds(i), i, 10000 + (i % 7))).ToList();
        await WithAsync(async p => await Store(p).AppendAsync(rows, Ct));
        var before = await ScalarAsync<string>("SELECT md5(string_agg(concat_ws('|', ts, seq, rx_mono_ms, flags, bid_px, bid_qty, bid_ord, ask_px, ask_qty, ask_ord), ';' ORDER BY ts)) FROM market.depth_snapshots");
        (await ScalarAsync<long>("SELECT count(*) FROM (SELECT compress_chunk(c) FROM show_chunks('market.depth_snapshots') c) x")).ShouldBeGreaterThan(0);
        var after = await ScalarAsync<string>("SELECT md5(string_agg(concat_ws('|', ts, seq, rx_mono_ms, flags, bid_px, bid_qty, bid_ord, ask_px, ask_qty, ask_ord), ';' ORDER BY ts)) FROM market.depth_snapshots");

        after.ShouldBe(before);
        (await ScalarAsync<long>("SELECT count(*) FROM market.depth_snapshots")).ShouldBe(600);
        (await ScalarAsync<long>("SELECT count(*) FROM timescaledb_information.chunks WHERE hypertable_name = 'depth_snapshots' AND is_compressed")).ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task The_database_enforces_the_shape_of_a_snapshot_a_session_and_a_set()
    {
        await using var connection = await OpenAsync();
        const string Insert = "INSERT INTO market.depth_snapshots (ts, instrument_id, seq, rx_mono_ms, flags, bid_px, bid_qty, bid_ord, ask_px, ask_qty, ask_ord) VALUES (now(), gen_random_uuid(), 1, 1, {0}, {1}, {1}, {1}, {1}, {1}, {1})";
        var twenty = "array_fill(1, ARRAY[20])";
        var nineteen = "array_fill(1, ARRAY[19])";
        await ExecuteAsync(connection, string.Format(System.Globalization.CultureInfo.InvariantCulture, Insert, 15, twenty));
        await Rejected(connection, string.Format(System.Globalization.CultureInfo.InvariantCulture, Insert, 16, twenty));
        await Rejected(connection, string.Format(System.Globalization.CultureInfo.InvariantCulture, Insert, -1, twenty));
        await Rejected(connection, string.Format(System.Globalization.CultureInfo.InvariantCulture, Insert, 0, nineteen));
        await Rejected(connection, "INSERT INTO market.depth_sessions (session_date, started_at, ended_at, end_reason, instruments, snapshots, packets, dropped_rows, gap_seconds) VALUES ('2026-10-12', now(), now(), 'sleepy', 1, 1, 1, 0, 0)");
        await Rejected(connection, "INSERT INTO market.depth_session_instruments (session_date, instrument_id, reason, rank) VALUES ('2026-10-12', gen_random_uuid(), 'liquid', 1)");
        await Rejected(connection, "INSERT INTO market.depth_session_instruments (session_date, instrument_id, reason, rank) VALUES ('2026-10-12', gen_random_uuid(), 'armed', 0)");
    }

    private static async Task Rejected(NpgsqlConnection connection, string sql)
    {
        var failure = await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connection, sql));
        failure.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
    }
}
