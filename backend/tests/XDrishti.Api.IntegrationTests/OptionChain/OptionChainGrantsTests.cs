using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;

namespace XDrishti.Api.IntegrationTests.OptionChain;

/// <summary>
/// P14-02 criterion 9: the four chain tables are insert-only for the runtime role <c>xd_app</c> - it can read and insert, and can neither update, delete nor truncate (a hypertable chunk included); the execution role and the read-only roles have no right on them.
/// On the dedicated throw-away server of <see cref="ResearchGrantsTests"/>, migrated by the owner with the init script's default privileges.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class OptionChainGrantsTests(ResearchGrantsTests.DedicatedServer server) : IClassFixture<ResearchGrantsTests.DedicatedServer>
{
    private static readonly string[] Tables = ["chain_snapshots", "chain_requests", "chain_sessions", "chain_recorder_switch"];

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
    public async Task The_runtime_role_reads_and_inserts_and_can_neither_change_nor_delete_nor_truncate_any_chain_table()
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
    public async Task The_execution_and_the_read_only_roles_have_no_right_on_any_chain_table()
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
        (await SqlStateAsync(server.App, "INSERT INTO options.chain_snapshots (ts, underlying_id, expiry, strike_px, side, rx_at, security_id, underlying_px, flags) VALUES (now(), gen_random_uuid(), '2026-10-20', 100, 'CE', now(), 1, 100, 0)")).ShouldBeNull();
        (await SqlStateAsync(server.App, "UPDATE options.chain_snapshots SET underlying_px = 2")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "DELETE FROM options.chain_snapshots")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "TRUNCATE options.chain_snapshots")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "INSERT INTO options.chain_recorder_switch (enabled, actor, reason) VALUES (true, 'owner:probe@example.invalid', 'probe')")).ShouldBeNull();
        (await SqlStateAsync(server.App, "UPDATE options.chain_recorder_switch SET enabled = false")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "INSERT INTO options.chain_sessions (session_date, started_at, ended_at, end_reason, slots, requests, ok, gaps, rows) VALUES ('2026-10-12', now(), now(), 'window_end', 1, 1, 1, 0, 1)")).ShouldBeNull();
        (await SqlStateAsync(server.App, "DELETE FROM options.chain_sessions")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "INSERT INTO options.chain_requests (slot_ts, kind, underlying_id, expiry, outcome, rows, latency_ms, requested_at) VALUES (now(), 'chain', gen_random_uuid(), '2026-10-20', 'ok', 1, 1, now())")).ShouldBeNull();
        (await SqlStateAsync(server.App, "UPDATE options.chain_requests SET rows = 2")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "SELECT COALESCE(sum(total_bytes), 0)::bigint FROM hypertable_detailed_size('options.chain_snapshots')")).ShouldBeNull();
    }
}
