using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;
using XDrishti.Application.Abstractions.OptionBackfill;
using XDrishti.Infrastructure.OptionBackfill;

namespace XDrishti.Api.IntegrationTests.OptionBackfill;

/// <summary>
/// P14-03 criterion 8: the four backfill tables are insert-only for the runtime role <c>xd_app</c> - it can read and insert, and can neither update, delete nor truncate (a hypertable chunk included); the execution role and the read-only roles have no right on them.
/// On the dedicated throw-away server of <see cref="ResearchGrantsTests"/>, migrated by the owner with the init script's default privileges.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class OptionBackfillGrantsTests(ResearchGrantsTests.DedicatedServer server) : IClassFixture<ResearchGrantsTests.DedicatedServer>
{
    private static readonly string[] Tables = ["rolling_bars", "fetch_log", "backfill_switch", "backfill_probe_ack"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<string?> SqlStateAsync(string connectionString, string sql)
    {
        try
        {
            await StoreKit.ExecuteAsync(connectionString, sql, Ct);
            return null;
        }
        catch (PostgresException failure)
        {
            return failure.SqlState;
        }
    }

    [Fact]
    public async Task The_runtime_role_reads_and_inserts_and_can_neither_change_nor_delete_nor_truncate_any_backfill_table()
    {
        await using var connection = new NpgsqlConnection(server.Owner);
        await connection.OpenAsync(Ct);
        foreach (var table in Tables)
        {
            foreach (var (privilege, expected) in new[] { ("SELECT", true), ("INSERT", true), ("UPDATE", false), ("DELETE", false), ("TRUNCATE", false) })
            {
                await using var check = new NpgsqlCommand("SELECT has_table_privilege('xd_app', @t, @p)", connection);
                check.Parameters.AddWithValue("t", "options." + table);
                check.Parameters.AddWithValue("p", privilege);
                ((bool)(await check.ExecuteScalarAsync(Ct))!).ShouldBe(expected, table + " " + privilege);
            }
        }
    }

    [Fact]
    public async Task The_execution_and_the_read_only_roles_have_no_right_on_any_backfill_table()
    {
        await using var connection = new NpgsqlConnection(server.Owner);
        await connection.OpenAsync(Ct);
        foreach (var role in new[] { "xd_exec", "xd_mcp_read", "xd_mcp_sql" })
        {
            foreach (var table in Tables)
            {
                await using var check = new NpgsqlCommand("SELECT CASE WHEN EXISTS (SELECT 1 FROM pg_roles WHERE rolname = @r) THEN has_table_privilege(@r, @t, 'SELECT') OR has_table_privilege(@r, @t, 'INSERT') ELSE false END", connection);
                check.Parameters.AddWithValue("r", role);
                check.Parameters.AddWithValue("t", "options." + table);
                ((bool)(await check.ExecuteScalarAsync(Ct))!).ShouldBeFalse(role + " " + table);
            }
        }
    }

    [Fact]
    public async Task The_runtime_role_inserts_into_a_hypertable_chunk_and_cannot_change_delete_or_truncate_it()
    {
        (await SqlStateAsync(server.App, "INSERT INTO options.rolling_bars (ts, underlying_id, expiry_flag, expiry_code, atm_offset, side, interval_minutes, strike_px, spot_px, open_px, high_px, low_px, close_px, flags) VALUES (now(), gen_random_uuid(), 'W', 1, 0, 'CE', 5, 100, 100, 1, 1, 1, 1, 0)")).ShouldBeNull();
        (await SqlStateAsync(server.App, "UPDATE options.rolling_bars SET spot_px = 2")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "DELETE FROM options.rolling_bars")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "TRUNCATE options.rolling_bars")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "INSERT INTO options.backfill_switch (enabled, actor, reason) VALUES (true, 'owner:probe@example.invalid', 'probe')")).ShouldBeNull();
        (await SqlStateAsync(server.App, "UPDATE options.backfill_switch SET enabled = false")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "INSERT INTO options.backfill_probe_ack (actor, note) VALUES ('owner:probe@example.invalid', 'probe')")).ShouldBeNull();
        (await SqlStateAsync(server.App, "DELETE FROM options.backfill_probe_ack")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "INSERT INTO options.fetch_log (underlying_id, expiry_flag, expiry_code, atm_offset, side, interval_minutes, window_from, window_to, attempt, started_at, ended_at, outcome, bars, latency_ms) VALUES (gen_random_uuid(), 'M', 1, 0, 'CE', 5, '2026-09-01', '2026-10-01', 1, now(), now(), 'ok', 1, 1)")).ShouldBeNull();
        (await SqlStateAsync(server.App, "UPDATE options.fetch_log SET bars = 2")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "DELETE FROM options.fetch_log")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "SELECT COALESCE(sum(total_bytes), 0)::bigint FROM hypertable_detailed_size('options.rolling_bars')")).ShouldBeNull();
    }

    [Fact]
    public async Task The_runtime_role_cannot_change_delete_or_truncate_a_chunk_by_its_own_name_either()
    {
        await using var owner = new NpgsqlConnection(server.Owner);
        await owner.OpenAsync(Ct);
        await using (var seed = new NpgsqlCommand("INSERT INTO options.rolling_bars (ts, underlying_id, expiry_flag, expiry_code, atm_offset, side, interval_minutes, strike_px, spot_px, open_px, high_px, low_px, close_px, flags) VALUES ('2021-12-01 04:00+00', gen_random_uuid(), 'W', 1, 0, 'CE', 5, 100, 100, 1, 1, 1, 1, 0)", owner))
        {
            await seed.ExecuteNonQueryAsync(Ct);
        }

        await using var names = new NpgsqlCommand("SELECT format('%I.%I', chunk_schema, chunk_name) FROM timescaledb_information.chunks WHERE hypertable_schema = 'options' AND hypertable_name = 'rolling_bars'", owner);
        var chunks = new List<string>();
        await using (var reader = await names.ExecuteReaderAsync(Ct))
        {
            while (await reader.ReadAsync(Ct))
            {
                chunks.Add(reader.GetString(0));
            }
        }

        chunks.ShouldNotBeEmpty();
        foreach (var chunk in chunks)
        {
            (await SqlStateAsync(server.App, $"UPDATE {chunk} SET spot_px = 2")).ShouldBe("42501", chunk);
            (await SqlStateAsync(server.App, $"DELETE FROM {chunk}")).ShouldBe("42501", chunk);
            (await SqlStateAsync(server.App, $"TRUNCATE {chunk}")).ShouldBe("42501", chunk);
        }
    }

    [Fact]
    public async Task The_insert_only_runtime_role_can_add_an_overlapping_and_a_new_window_into_a_chunk_that_is_already_compressed()
    {
        // Every chunk of a five-year backfill is old, so the compression policy compresses it before the later keys are written into it. As xd_app (insert and select only) the store must still insert the new bars and skip the old ones.
        await using var dataSource = NpgsqlDataSource.Create(server.App);
        var store = new RollingBarStore(dataSource);
        var underlying = Guid.NewGuid();
        var day = new DateTimeOffset(2021, 12, 6, 9, 15, 0, TimeSpan.FromMinutes(330));
        BackfillBar Bar(int minutes, int offset = 0) => new(day.AddMinutes(minutes), underlying, 'W', 1, offset, "CE", 5, 2_550_000, 2_501_230, 4_820, 5_230, 4_790, 5_105, 1, 1, 12.3f, 0);
        BackfillFetchRecord Attempt(int number, int bars) => new(new BackfillKey(underlying, 'W', 1, 0, "CE", 5, new DateOnly(2021, 11, 20)), number, day, day, "ok", 200, null, bars, 1);

        (await store.AppendAsync(Attempt(1, 3), [Bar(0), Bar(5), Bar(10)], Ct)).ShouldBe(3);
        await StoreKit.ExecuteAsync(server.Owner, "SELECT count(*) FROM (SELECT compress_chunk(c) FROM show_chunks('options.rolling_bars') c) x", Ct);
        await using (var check = new NpgsqlConnection(server.Owner))
        {
            await check.OpenAsync(Ct);
            await using var compressed = new NpgsqlCommand("SELECT count(*) FROM timescaledb_information.chunks WHERE hypertable_schema = 'options' AND hypertable_name = 'rolling_bars' AND is_compressed", check);
            ((long)(await compressed.ExecuteScalarAsync(Ct))!).ShouldBeGreaterThan(0, "the chunk really is compressed before the second window is written");
        }

        (await store.AppendAsync(Attempt(2, 5), [Bar(0), Bar(5), Bar(10), Bar(15), Bar(20)], Ct)).ShouldBe(2, "the overlap is skipped, the two new bars go into the compressed chunk");
        (await store.AppendAsync(Attempt(3, 2), [Bar(0, 1), Bar(5, 1)], Ct)).ShouldBe(2, "another offset of the same time range is new too");
        await using var owner = new NpgsqlConnection(server.Owner);
        await owner.OpenAsync(Ct);
        await using var count = new NpgsqlCommand("SELECT count(*) FROM options.rolling_bars WHERE underlying_id = @u", owner);
        count.Parameters.AddWithValue("u", underlying);
        ((long)(await count.ExecuteScalarAsync(Ct))!).ShouldBe(7);
    }
}
