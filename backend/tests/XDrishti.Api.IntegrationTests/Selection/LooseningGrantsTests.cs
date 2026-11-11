using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;

namespace XDrishti.Api.IntegrationTests.Selection;

/// <summary>BK-10b: the two loosening tables are insert-only for the runtime role <c>xd_app</c> (it can read and insert, and can neither update, delete nor truncate). On the dedicated throw-away server of <see cref="ResearchGrantsTests"/>.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class LooseningGrantsTests(ResearchGrantsTests.DedicatedServer server) : IClassFixture<ResearchGrantsTests.DedicatedServer>
{
    private static readonly string[] Tables = ["loosening_dismissals", "loosening_settings"];

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
                (await HasAsync("xd_app", "trading." + table, privilege)).ShouldBe(expected, table + " " + privilege);
            }
        }
    }

    [Fact]
    public async Task As_the_runtime_role_an_insert_works_with_the_trigger_drawing_the_id_and_an_update_or_a_delete_is_refused()
    {
        await using var connection = new NpgsqlConnection(server.App);
        await connection.OpenAsync(Ct);
        await using (var insert = new NpgsqlCommand("INSERT INTO trading.loosening_settings (auto_dismiss, changed_by, hash) VALUES (true, 'owner:x@y', 'h')", connection))
        {
            (await insert.ExecuteNonQueryAsync(Ct)).ShouldBe(1);
        }

        await using (var insert = new NpgsqlCommand("INSERT INTO trading.loosening_dismissals (notice_source, notice_id, actor, setting_id, setting_hash) SELECT 'sizing', 1, 'auto', id, 'h' FROM trading.loosening_settings", connection))
        {
            (await insert.ExecuteNonQueryAsync(Ct)).ShouldBe(1);
        }

        foreach (var sql in new[] { "UPDATE trading.loosening_settings SET auto_dismiss = false", "DELETE FROM trading.loosening_settings", "UPDATE trading.loosening_dismissals SET actor = 'owner:x@y', setting_id = NULL, setting_hash = NULL", "DELETE FROM trading.loosening_dismissals" })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            var failure = await Should.ThrowAsync<PostgresException>(async () => await command.ExecuteNonQueryAsync(Ct));
            failure.SqlState.ShouldBe("42501", sql);
        }
    }
}
