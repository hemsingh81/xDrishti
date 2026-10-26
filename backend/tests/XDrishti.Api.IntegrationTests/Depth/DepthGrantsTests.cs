using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;

namespace XDrishti.Api.IntegrationTests.Depth;

/// <summary>
/// P7-16 criterion 9: the four depth tables are insert-only for the runtime role <c>xd_app</c> - it can read and insert, and can neither update, delete nor truncate (a chunk of the hypertable included). Runs on the dedicated throw-away server of
/// <see cref="ResearchGrantsTests"/>, where the role has the default privileges of the init script and the chain is migrated by the owner. `market` is not re-granted: a later market migration that re-grants the schema and forgets
/// <c>MakeAppendOnlyForAppRole</c> fails here.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class DepthGrantsTests(ResearchGrantsTests.DedicatedServer server) : IClassFixture<ResearchGrantsTests.DedicatedServer>
{
    private static readonly string[] Tables = ["depth_snapshots", "depth_session_instruments", "depth_sessions", "depth_recorder_switch"];

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
    public async Task The_runtime_role_reads_and_inserts_and_can_neither_change_nor_delete_nor_truncate_any_depth_table()
    {
        await using var connection = new NpgsqlConnection(server.Owner);
        await connection.OpenAsync(Ct);
        foreach (var table in Tables)
        {
            foreach (var (privilege, expected) in new[] { ("SELECT", true), ("INSERT", true), ("UPDATE", false), ("DELETE", false), ("TRUNCATE", false) })
            {
                await using var check = new NpgsqlCommand("SELECT has_table_privilege('xd_app', @t, @p)", connection);
                check.Parameters.AddWithValue("t", "market." + table);
                check.Parameters.AddWithValue("p", privilege);
                ((bool)(await check.ExecuteScalarAsync(Ct))!).ShouldBe(expected, table + " " + privilege);
            }
        }
    }

    [Fact]
    public async Task The_runtime_role_inserts_a_row_into_a_hypertable_chunk_and_cannot_change_or_delete_or_truncate_it_and_the_identity_works()
    {
        const string Snapshot = "INSERT INTO market.depth_snapshots (ts, instrument_id, seq, rx_mono_ms, flags, bid_px, bid_qty, bid_ord, ask_px, ask_qty, ask_ord) VALUES (now(), gen_random_uuid(), 1, 1, 0, array_fill(1, ARRAY[20]), array_fill(1, ARRAY[20]), array_fill(1, ARRAY[20]), array_fill(1, ARRAY[20]), array_fill(1, ARRAY[20]), array_fill(1, ARRAY[20]))";

        (await SqlStateAsync(server.App, Snapshot)).ShouldBeNull();
        (await SqlStateAsync(server.App, "SELECT count(*) FROM market.depth_snapshots")).ShouldBeNull();
        (await SqlStateAsync(server.App, "UPDATE market.depth_snapshots SET seq = 2")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "DELETE FROM market.depth_snapshots")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "TRUNCATE market.depth_snapshots")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "INSERT INTO market.depth_recorder_switch (enabled, actor, reason) VALUES (true, 'owner:probe@example.invalid', 'probe')")).ShouldBeNull();
        (await SqlStateAsync(server.App, "UPDATE market.depth_recorder_switch SET enabled = false")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "DELETE FROM market.depth_recorder_switch")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "INSERT INTO market.depth_sessions (session_date, started_at, ended_at, end_reason, instruments, snapshots, packets, dropped_rows, gap_seconds) VALUES ('2026-10-12', now(), now(), 'window_end', 1, 1, 1, 0, 0)")).ShouldBeNull();
        (await SqlStateAsync(server.App, "UPDATE market.depth_sessions SET snapshots = 0")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "INSERT INTO market.depth_session_instruments (session_date, instrument_id, reason, rank) VALUES ('2026-10-12', gen_random_uuid(), 'armed', 1)")).ShouldBeNull();
        (await SqlStateAsync(server.App, "DELETE FROM market.depth_session_instruments")).ShouldBe("42501");
    }

    [Fact]
    public async Task The_runtime_role_can_read_the_stored_size_the_disk_guard_asks_for()
    {
        (await SqlStateAsync(server.App, "SELECT COALESCE(sum(total_bytes), 0)::bigint FROM hypertable_detailed_size('market.depth_snapshots')")).ShouldBeNull();
    }
}
