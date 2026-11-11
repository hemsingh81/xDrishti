using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;

namespace XDrishti.Api.IntegrationTests.OpeningBars;

/// <summary>
/// BK-03b criterion 8: the three opening-minute tables are insert-only for the runtime role <c>xd_app</c> - it can read and insert, and can neither update, delete nor truncate. On the dedicated throw-away server of <see cref="ResearchGrantsTests"/>,
/// migrated by the owner with the init script's default privileges.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class OpeningBarsGrantsTests(ResearchGrantsTests.DedicatedServer server) : IClassFixture<ResearchGrantsTests.DedicatedServer>
{
    private static readonly string[] Tables = ["bars_1m_live", "opening_bar_events", "opening_bars_switch"];

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
    public async Task The_runtime_role_reads_and_inserts_and_can_neither_change_nor_delete_nor_truncate_any_opening_table()
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
    public async Task The_runtime_role_inserts_a_bar_with_on_conflict_do_nothing_and_cannot_update_or_delete_it()
    {
        const string Insert = "INSERT INTO market.bars_1m_live (instrument_id, minute_start, o, h, l, c, v, packets) VALUES ('00000000-0000-0000-0000-000000000001', '2026-10-12 03:45:00+00', 100, 101, 99, 100, 10, 1) ON CONFLICT (instrument_id, minute_start) DO NOTHING";

        (await SqlStateAsync(server.App, Insert)).ShouldBeNull();
        (await SqlStateAsync(server.App, Insert)).ShouldBeNull("a second write of the minute is left alone");
        (await SqlStateAsync(server.App, "UPDATE market.bars_1m_live SET v = 11")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "DELETE FROM market.bars_1m_live")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "INSERT INTO market.opening_bar_events (session_date, kind, from_at) VALUES ('2026-10-12', 'started', now())")).ShouldBeNull();
        (await SqlStateAsync(server.App, "UPDATE market.opening_bar_events SET kind = 'gap'")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "INSERT INTO market.opening_bars_switch (enabled, actor, reason) VALUES (true, 'owner:probe@example.invalid', 'probe')")).ShouldBeNull();
        (await SqlStateAsync(server.App, "DELETE FROM market.opening_bars_switch")).ShouldBe("42501");
    }

    [Fact]
    public async Task The_runtime_role_runs_the_retention_function_and_still_cannot_delete_directly()
    {
        (await SqlStateAsync(server.App, "SELECT market.prune_opening_bars(60)")).ShouldBeNull();
        (await SqlStateAsync(server.App, "DELETE FROM market.bars_1m_live WHERE false")).ShouldBe("42501");
    }
}
