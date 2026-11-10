using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;

namespace XDrishti.Api.IntegrationTests.Planning;

/// <summary>Spec AU-16: <c>planning.auto_scopes</c> is insert-only for the runtime role <c>xd_app</c>: it reads and inserts a scope version and can neither change nor delete one. Runs on the throw-away server of <see cref="ResearchGrantsTests"/>.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class AutoScopeGrantsTests(ResearchGrantsTests.DedicatedServer server) : IClassFixture<ResearchGrantsTests.DedicatedServer>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("SELECT", true)]
    [InlineData("INSERT", true)]
    [InlineData("UPDATE", false)]
    [InlineData("DELETE", false)]
    [InlineData("TRUNCATE", false)]
    public async Task The_runtime_role_reads_and_inserts_a_scope_version_and_can_neither_change_nor_delete_it(string privilege, bool expected)
    {
        await using var connection = new NpgsqlConnection(server.Owner);
        await connection.OpenAsync(Ct);
        await using var check = new NpgsqlCommand("SELECT has_table_privilege('xd_app', 'planning.auto_scopes', @p)", connection);
        check.Parameters.AddWithValue("p", privilege);

        ((bool)(await check.ExecuteScalarAsync(Ct))!).ShouldBe(expected, privilege);
    }

    [Fact]
    public async Task A_scope_with_no_entries_or_a_non_owner_author_is_refused_by_the_table()
    {
        await using var connection = new NpgsqlConnection(server.Owner);
        await connection.OpenAsync(Ct);

        var empty = await Insert(connection, "'{}'::uuid[]", "'{}'::uuid[]", "owner:a@x.test");
        var auto = await Insert(connection, "ARRAY[gen_random_uuid()]", "'{}'::uuid[]", "auto");
        var fine = await Insert(connection, "ARRAY[gen_random_uuid()]", "'{}'::uuid[]", "owner:a@x.test");

        (empty, auto, fine).ShouldBe(("23514", "23514", null));
    }

    private static async Task<string?> Insert(NpgsqlConnection connection, string baskets, string instruments, string by)
    {
        try
        {
            await using var command = new NpgsqlCommand($"INSERT INTO planning.auto_scopes (basket_ids, instrument_ids, scope_hash, created_by, created_at) VALUES ({baskets}, {instruments}, repeat('a', 64), @by, now())", connection);
            command.Parameters.AddWithValue("by", by);
            await command.ExecuteNonQueryAsync(Ct);
            return null;
        }
        catch (PostgresException failure)
        {
            return failure.SqlState;
        }
    }
}
