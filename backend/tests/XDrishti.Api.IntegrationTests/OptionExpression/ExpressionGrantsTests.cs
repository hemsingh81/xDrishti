using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;
using XDrishti.Application.Abstractions.OptionExpression;
using XDrishti.Domain.Options.Remark;
using XDrishti.Infrastructure.OptionExpression;

namespace XDrishti.Api.IntegrationTests.OptionExpression;

/// <summary>
/// P14-08 criterion 11 (grants): the two tables are insert-only for the runtime role <c>xd_app</c> (read and insert; no update, delete or truncate), the execution and read-only roles have nothing on them, and the store works as <c>xd_app</c>. On the dedicated throw-away server of
/// <see cref="ResearchGrantsTests"/>, migrated by the owner with the init script's default privileges.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ExpressionGrantsTests(ResearchGrantsTests.DedicatedServer server) : IClassFixture<ResearchGrantsTests.DedicatedServer>
{
    private static readonly string[] Tables = ["research.expression_runs", "research.expression_trades"];

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
    public async Task The_store_works_as_the_runtime_role_a_second_insert_writes_nothing_and_the_refused_statements_are_refused()
    {
        await using var dataSource = NpgsqlDataSource.Create(server.App);
        var store = new ExpressionTradeStore(dataSource);
        var entry = new DateTimeOffset(2025, 6, 10, 9, 20, 0, TimeSpan.FromMinutes(330));
        var runId = new string('e', 64);
        var run = new ExpressionRunRow(runId, new string('c', 64), new string('d', 64), "expr-1", "{\"format\":\"expr-remark/1\"}", 2, 1, "{\"no_series\":1}", 0.0, ["basis: gross"]);
        RemarkRow[] rows =
        [
            new("T1", RemarkFates.Paired, "CE", 'W', 1, new DateOnly(2025, 6, 12), "derived", 2_200_000, entry, entry.AddMinutes(40), 6000, 6500, 12.5, 62.50m, null, 0.0625, 2.0, "target", 75, "sizable", ["basis: gross"]),
            new("T2", RemarkFates.NoSeries, "PE", null, null, null, "none", null, entry, entry.AddMinutes(40), null, null, null, null, null, null, null, "stop", null, RemarkRow.NotApplicable, []),
        ];

        (await store.ExistsAsync(runId, Ct)).ShouldBeFalse();
        (await store.AppendAsync(run, rows, Ct)).ShouldBeTrue();
        (await store.ExistsAsync(runId, Ct)).ShouldBeTrue();
        (await store.AppendAsync(run, rows, Ct)).ShouldBeFalse();
        await Should.ThrowAsync<InvalidOperationException>(async () => await store.AppendAsync(run with { RunId = new string('f', 64) }, rows[..1], Ct));

        (await StoreKit.CountAsync(server.Owner, "SELECT count(*) FROM research.expression_trades WHERE run_id = @r", Ct, ("r", runId))).ShouldBe(2);
        (await StoreKit.CountAsync(server.Owner, "SELECT count(*) FROM research.expression_runs WHERE run_id = @r", Ct, ("r", new string('f', 64)))).ShouldBe(0, "the refused run left nothing behind");
        (await SqlStateAsync(server.App, "UPDATE research.expression_trades SET fate = 'no_series'")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "DELETE FROM research.expression_trades")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "TRUNCATE research.expression_trades, research.expression_runs")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "UPDATE research.expression_runs SET paired = 0")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "DELETE FROM research.expression_runs")).ShouldBe("42501");
    }
}
