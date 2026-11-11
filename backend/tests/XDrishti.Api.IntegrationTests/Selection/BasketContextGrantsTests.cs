using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;

namespace XDrishti.Api.IntegrationTests.Selection;

/// <summary>BK-08 (ADR 0260): the daily basket context and the basket settings history are insert-only for the runtime role <c>xd_app</c> (it can read and insert, and can neither update, delete nor truncate). On the dedicated throw-away server of <see cref="ResearchGrantsTests"/>.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class BasketContextGrantsTests(ResearchGrantsTests.DedicatedServer server) : IClassFixture<ResearchGrantsTests.DedicatedServer>
{
    private static readonly string[] Tables = ["market.basket_context_daily", "selection.basket_settings_changes"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<bool> HasAsync(string role, string table, string privilege)
    {
        await using var connection = new NpgsqlConnection(server.Owner);
        await connection.OpenAsync(Ct);
        await using var check = new NpgsqlCommand("SELECT has_table_privilege(@r, @t, @p)", connection);
        check.Parameters.AddWithValue("r", role);
        check.Parameters.AddWithValue("t", table);
        check.Parameters.AddWithValue("p", privilege);
        return (bool)(await check.ExecuteScalarAsync(Ct))!;
    }

    [Fact]
    public async Task The_runtime_role_reads_and_inserts_and_can_neither_change_nor_delete_nor_truncate()
    {
        foreach (var table in Tables)
        {
            foreach (var (privilege, expected) in new[] { ("SELECT", true), ("INSERT", true), ("UPDATE", false), ("DELETE", false), ("TRUNCATE", false) })
            {
                (await HasAsync("xd_app", table, privilege)).ShouldBe(expected, table + " " + privilege);
            }
        }
    }

    [Fact]
    public async Task As_the_runtime_role_an_insert_works_and_an_update_or_a_delete_is_refused()
    {
        await using var connection = new NpgsqlConnection(server.App);
        await connection.OpenAsync(Ct);
        await using (var insert = new NpgsqlCommand("INSERT INTO market.basket_context_daily VALUES (gen_random_uuid(), '2026-10-09', 10, 8, 0.5, NULL, NULL, NULL, NULL, NULL, NULL, 'value', 'single_snapshot', now())", connection))
        {
            (await insert.ExecuteNonQueryAsync(Ct)).ShouldBe(1);
        }

        await using (var insert = new NpgsqlCommand("INSERT INTO selection.basket_settings_changes (key, old_value, new_value, direction, changed_by, hash) VALUES ('selection.cluster_rho', '0.7', '0.8', 'loosen', 'owner:x', 'h')", connection))
        {
            (await insert.ExecuteNonQueryAsync(Ct)).ShouldBe(1);
        }

        foreach (var sql in new[] { "UPDATE market.basket_context_daily SET b20 = 0.9", "DELETE FROM market.basket_context_daily", "UPDATE selection.basket_settings_changes SET direction = 'tighten'", "DELETE FROM selection.basket_settings_changes" })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            var failure = await Should.ThrowAsync<PostgresException>(async () => await command.ExecuteNonQueryAsync(Ct));
            failure.SqlState.ShouldBe("42501", sql);
        }
    }
}
