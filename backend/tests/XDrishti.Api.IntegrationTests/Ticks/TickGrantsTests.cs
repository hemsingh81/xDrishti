using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;

namespace XDrishti.Api.IntegrationTests.Ticks;

/// <summary>
/// P7-16b criterion 10: the four tick tables are insert-only for the runtime role <c>xd_app</c> - it can read and insert, and can neither update, delete nor truncate (a hypertable chunk included). On the dedicated throw-away server of
/// <see cref="ResearchGrantsTests"/>, migrated by the owner with the init script's default privileges.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class TickGrantsTests(ResearchGrantsTests.DedicatedServer server) : IClassFixture<ResearchGrantsTests.DedicatedServer>
{
    private static readonly string[] Tables = ["trade_ticks", "tick_session_instruments", "tick_sessions", "tick_recorder_switch"];

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
    public async Task The_runtime_role_reads_and_inserts_and_can_neither_change_nor_delete_nor_truncate_any_tick_table()
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
    public async Task The_execution_and_the_read_only_roles_have_no_right_on_any_tick_table()
    {
        await using var connection = new NpgsqlConnection(server.Owner);
        await connection.OpenAsync(Ct);
        foreach (var role in new[] { "xd_exec", "xd_mcp_read" })
        {
            foreach (var table in Tables)
            {
                await using var check = new NpgsqlCommand("SELECT CASE WHEN EXISTS (SELECT 1 FROM pg_roles WHERE rolname = @r) THEN has_table_privilege(@r, @t, 'SELECT') ELSE false END", connection);
                check.Parameters.AddWithValue("r", role);
                check.Parameters.AddWithValue("t", "market." + table);
                ((bool)(await check.ExecuteScalarAsync(Ct))!).ShouldBeFalse(role + " " + table);
            }
        }
    }

    [Fact]
    public async Task The_runtime_role_inserts_into_a_hypertable_chunk_and_cannot_change_delete_or_truncate_it()
    {
        (await SqlStateAsync(server.App, "INSERT INTO market.trade_ticks (ts, instrument_id, rx_mono_ms, rx_seq, ltt_epoch, ltp_px, ltq, cum_vol, flags) VALUES (now(), gen_random_uuid(), 1, 1, 1, 100, 1, 1, 1)")).ShouldBeNull();
        (await SqlStateAsync(server.App, "UPDATE market.trade_ticks SET ltq = 2")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "DELETE FROM market.trade_ticks")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "TRUNCATE market.trade_ticks")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "INSERT INTO market.tick_recorder_switch (enabled, actor, reason) VALUES (true, 'owner:probe@example.invalid', 'probe')")).ShouldBeNull();
        (await SqlStateAsync(server.App, "UPDATE market.tick_recorder_switch SET enabled = false")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "INSERT INTO market.tick_sessions (session_date, started_at, ended_at, end_reason, instruments, packets, rows, unchanged, dropped_rows, gap_seconds) VALUES ('2026-10-12', now(), now(), 'window_end', 1, 1, 1, 0, 0, 0)")).ShouldBeNull();
        (await SqlStateAsync(server.App, "DELETE FROM market.tick_sessions")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "INSERT INTO market.tick_session_instruments (session_date, instrument_id, source, reason, rank) VALUES ('2026-10-12', gen_random_uuid(), 'built', 'armed', 1)")).ShouldBeNull();
        (await SqlStateAsync(server.App, "UPDATE market.tick_session_instruments SET rank = 2")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "SELECT COALESCE(sum(total_bytes), 0)::bigint FROM hypertable_detailed_size('market.trade_ticks')")).ShouldBeNull();
    }
}
