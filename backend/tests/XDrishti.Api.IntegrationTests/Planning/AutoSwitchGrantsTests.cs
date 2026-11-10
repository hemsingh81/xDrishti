using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;

namespace XDrishti.Api.IntegrationTests.Planning;

/// <summary>Spec AU-17: <c>planning.auto_switch_changes</c> is insert-only for the runtime role <c>xd_app</c>, and the table refuses a row that is not the owner's or <c>auto</c>. Runs on the throw-away server of <see cref="ResearchGrantsTests"/>.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class AutoSwitchGrantsTests(ResearchGrantsTests.DedicatedServer server) : IClassFixture<ResearchGrantsTests.DedicatedServer>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("SELECT", true)]
    [InlineData("INSERT", true)]
    [InlineData("UPDATE", false)]
    [InlineData("DELETE", false)]
    [InlineData("TRUNCATE", false)]
    public async Task The_runtime_role_reads_and_inserts_a_change_and_can_neither_change_nor_delete_it(string privilege, bool expected)
    {
        await using var connection = new NpgsqlConnection(server.Owner);
        await connection.OpenAsync(Ct);
        await using var check = new NpgsqlCommand("SELECT has_table_privilege('xd_app', 'planning.auto_switch_changes', @p)", connection);
        check.Parameters.AddWithValue("p", privilege);

        ((bool)(await check.ExecuteScalarAsync(Ct))!).ShouldBe(expected, privilege);
    }

    [Fact]
    public async Task A_change_by_anyone_but_the_owner_or_auto_and_a_kind_other_than_on_or_off_are_refused_by_the_table()
    {
        await using var connection = new NpgsqlConnection(server.Owner);
        await connection.OpenAsync(Ct);

        var system = await Insert(connection, "on", "system:nightly");
        var kind = await Insert(connection, "approve", "owner:a@x.test");
        var owner = await Insert(connection, "on", "owner:a@x.test");
        var auto = await Insert(connection, "off", "auto");

        (system, kind, owner, auto).ShouldBe(("23514", "23514", null, null));
    }

    private static async Task<string?> Insert(NpgsqlConnection connection, string kind, string actor)
    {
        try
        {
            await using var command = new NpgsqlCommand("INSERT INTO planning.auto_switch_changes (kind, actor, at, changed, snapshot) VALUES (@kind, @actor, now(), '{}', '{}')", connection);
            command.Parameters.AddWithValue("kind", kind);
            command.Parameters.AddWithValue("actor", actor);
            await command.ExecuteNonQueryAsync(Ct);
            return null;
        }
        catch (PostgresException failure)
        {
            return failure.SqlState;
        }
    }
}
