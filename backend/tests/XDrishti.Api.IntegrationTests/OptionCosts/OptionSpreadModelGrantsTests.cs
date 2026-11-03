using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;
using XDrishti.Domain.Options.Costs;
using XDrishti.Infrastructure.OptionCosts;

namespace XDrishti.Api.IntegrationTests.OptionCosts;

/// <summary>
/// P14-07 criterion 8 (grants): the two model tables are insert-only for the runtime role <c>xd_app</c> (read and insert; no update, delete or truncate), the execution and read-only roles have nothing on them, and the store and the spread reader work as <c>xd_app</c>.
/// On the dedicated throw-away server of <see cref="ResearchGrantsTests"/>, migrated by the owner with the init script's default privileges.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class OptionSpreadModelGrantsTests(ResearchGrantsTests.DedicatedServer server) : IClassFixture<ResearchGrantsTests.DedicatedServer>
{
    private static readonly string[] Tables = ["research.option_spread_models", "research.option_spread_buckets"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<bool> HasAsync(string role, string table, string privilege)
    {
        await using var connection = new NpgsqlConnection(server.Owner);
        await connection.OpenAsync(Ct);
        await using var check = new NpgsqlCommand("SELECT CASE WHEN EXISTS (SELECT 1 FROM pg_roles WHERE rolname = @r) THEN has_table_privilege(@r, @t, @p) ELSE false END", connection);
        check.Parameters.AddWithValue("r", role);
        check.Parameters.AddWithValue("t", table);
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
        foreach (var table in Tables)
        {
            foreach (var (privilege, expected) in new[] { ("SELECT", true), ("INSERT", true), ("UPDATE", false), ("DELETE", false), ("TRUNCATE", false) })
            {
                (await HasAsync("xd_app", table, privilege)).ShouldBe(expected, table + " " + privilege);
            }
        }

        await StoreKit.ExecuteAsync(
            server.Owner,
            "DO $$ BEGIN IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_exec') THEN CREATE ROLE xd_exec NOLOGIN; END IF; IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_mcp_read') THEN CREATE ROLE xd_mcp_read NOLOGIN; END IF; IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_mcp_sql') THEN CREATE ROLE xd_mcp_sql NOLOGIN; END IF; END $$",
            Ct);
        foreach (var role in new[] { "xd_exec", "xd_mcp_read", "xd_mcp_sql" })
        {
            foreach (var table in Tables)
            {
                foreach (var privilege in new[] { "SELECT", "INSERT", "UPDATE", "DELETE" })
                {
                    (await HasAsync(role, table, privilege)).ShouldBeFalse($"{role} {table} {privilege}");
                }
            }
        }
    }

    [Fact]
    public async Task The_store_and_the_reader_work_as_the_runtime_role_and_the_refused_statements_are_refused()
    {
        await using var dataSource = NpgsqlDataSource.Create(server.App);
        var store = new SpreadModelStore(dataSource);
        var underlying = Guid.NewGuid();
        var model = new BuiltSpreadModel(new string('c', 64), underlying, 60, new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 1), SpreadModelId.SettingsJson(60, 200), [new SpreadBucketRow("0", "0-2", "10:00-14:30", 250, 0.8)]);

        (await store.AppendAsync(model, Ct)).ShouldBeTrue();
        (await store.AppendAsync(model, Ct)).ShouldBeFalse();

        (await store.LatestAsync(underlying, Ct)).ShouldNotBeNull().Lookup(new SpreadBucket("0", "0-2", "10:00-14:30")).ShouldBe(new SpreadBucketRow("0", "0-2", "10:00-14:30", 250, 0.8));
        (await new ChainSpreadReader(dataSource).QuotesAsync(underlying, new DateOnly(2026, 1, 1), Ct)).ShouldBeEmpty();
        (await SqlStateAsync(server.App, "UPDATE research.option_spread_models SET sessions = 1")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "DELETE FROM research.option_spread_models")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "TRUNCATE research.option_spread_models, research.option_spread_buckets")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "UPDATE research.option_spread_buckets SET n = 1")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "DELETE FROM research.option_spread_buckets")).ShouldBe("42501");
    }
}
