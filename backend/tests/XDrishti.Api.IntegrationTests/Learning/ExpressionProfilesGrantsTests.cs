using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;
using XDrishti.Domain.Learning;
using XDrishti.Infrastructure.Learning;

namespace XDrishti.Api.IntegrationTests.Learning;

/// <summary>
/// P14-06 criterion 4 (ADR 0193): <c>lab.expression_profiles</c> is insert-only for the runtime role <c>xd_app</c> (read and insert; no update, delete or truncate), the execution and read-only roles have nothing on it, and the store works as <c>xd_app</c>.
/// On the dedicated throw-away server of <see cref="ResearchGrantsTests"/>, migrated by the owner with the init script's default privileges.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ExpressionProfilesGrantsTests(ResearchGrantsTests.DedicatedServer server) : IClassFixture<ResearchGrantsTests.DedicatedServer>
{
    private const string Table = "lab.expression_profiles";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<bool> HasAsync(string role, string privilege)
    {
        await using var connection = new NpgsqlConnection(server.Owner);
        await connection.OpenAsync(Ct);
        await using var check = new NpgsqlCommand("SELECT CASE WHEN EXISTS (SELECT 1 FROM pg_roles WHERE rolname = @r) THEN has_table_privilege(@r, @t, @p) ELSE false END", connection);
        check.Parameters.AddWithValue("r", role);
        check.Parameters.AddWithValue("t", Table);
        check.Parameters.AddWithValue("p", privilege);
        return (bool)(await check.ExecuteScalarAsync(Ct))!;
    }

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
    public async Task The_runtime_role_reads_and_inserts_and_can_neither_change_nor_delete_nor_truncate_and_the_other_roles_hold_nothing()
    {
        foreach (var (privilege, expected) in new[] { ("SELECT", true), ("INSERT", true), ("UPDATE", false), ("DELETE", false), ("TRUNCATE", false) })
        {
            (await HasAsync("xd_app", privilege)).ShouldBe(expected, privilege);
        }

        await StoreKit.ExecuteAsync(
            server.Owner,
            "DO $$ BEGIN IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_exec') THEN CREATE ROLE xd_exec NOLOGIN; END IF; IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_mcp_read') THEN CREATE ROLE xd_mcp_read NOLOGIN; END IF; IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_mcp_sql') THEN CREATE ROLE xd_mcp_sql NOLOGIN; END IF; END $$",
            Ct);
        foreach (var role in new[] { "xd_exec", "xd_mcp_read", "xd_mcp_sql" })
        {
            foreach (var privilege in new[] { "SELECT", "INSERT", "UPDATE", "DELETE" })
            {
                (await HasAsync(role, privilege)).ShouldBeFalse($"{role} {privilege}");
            }
        }
    }

    [Fact]
    public async Task The_store_works_as_the_runtime_role_and_the_refused_statements_are_refused()
    {
        await using var dataSource = NpgsqlDataSource.Create(server.App);
        var store = new ExpressionProfileStore(dataSource);
        var profile = ExpressionProfile.Create(Expression.Pair, [new("lookback", 60), new("entry_z", 2.0m)]).Value;

        (await store.AppendAsync(profile, Ct)).ShouldBeTrue();
        (await store.AppendAsync(profile, Ct)).ShouldBeFalse();

        (await store.GetAsync(profile.Hash, Ct)).ShouldNotBeNull().Profile.CanonicalText.ShouldBe(profile.CanonicalText);
        (await store.ListAsync(Ct)).ShouldContain(p => p.Profile.Hash == profile.Hash);
        (await SqlStateAsync(server.App, "UPDATE lab.expression_profiles SET kind = 'cash'")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "DELETE FROM lab.expression_profiles")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "TRUNCATE lab.expression_profiles")).ShouldBe("42501");
    }
}
