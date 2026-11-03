using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;
using XDrishti.Application.Abstractions.Pairs;
using XDrishti.Infrastructure.Pairs;

namespace XDrishti.Api.IntegrationTests.Pairs;

/// <summary>
/// P14-13 criterion 8 (grants): the two tables are insert-only for the runtime role <c>xd_app</c> (read and insert; no update, delete or truncate), the execution and read-only roles have nothing on them, and the store works as <c>xd_app</c>. On the dedicated throw-away server of
/// <see cref="ResearchGrantsTests"/>, migrated by the owner with the init script's default privileges.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class PairResearchGrantsTests(ResearchGrantsTests.DedicatedServer server) : IClassFixture<ResearchGrantsTests.DedicatedServer>
{
    private static readonly string[] Tables = ["research.pair_formations", "research.pair_trades"];

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
        var store = new PairResearchStore(dataSource);
        var formation = new PairFormationRow(new string('c', 64), "pairs:intraday-1", "2026Q4", new DateOnly(2025, 9, 1), new DateOnly(2026, 9, 30), 3, 3, 0, 1, "{\"m\":3}");
        var entry = new DateTimeOffset(2026, 10, 1, 11, 0, 0, TimeSpan.FromMinutes(330));
        PairTradeRow Trade(int ordinal, decimal? net) => new(new string('d', 64), "NSE:A|NSE:B", ordinal, entry, entry.AddMinutes(10), "short_spread", 2.2, null, "exit", 991, 1000, 743.25m, net, 1.2, ["variant:e2.0/L5", "gross"]);

        (await store.AppendFormationAsync(formation, Ct)).ShouldBeTrue();
        (await store.AppendFormationAsync(formation, Ct)).ShouldBeFalse();
        (await store.AppendTradesAsync([Trade(1, null), Trade(2, 700.10m)], Ct)).ShouldBe(2);
        (await store.AppendTradesAsync([Trade(1, null), Trade(2, 700.10m)], Ct)).ShouldBe(0);

        var trades = await store.ReadTradesAsync("gross", Ct);
        trades.Count.ShouldBe(2);
        trades[0].Date.ShouldBe(new DateOnly(2026, 10, 1));
        trades[0].NetInr.ShouldBeNull();
        trades[1].NetInr.ShouldBe(700.10m);
        trades[0].Labels.ShouldBe(["variant:e2.0/L5", "gross"]);
        var stats = await store.ReadFormationsAsync("pairs:intraday-1", Ct);
        stats.Single().Stat.Selected.ShouldBe(1);
        stats[0].FormationId.ShouldBe(new string('c', 64));
        (await store.CountOtherTradesAsync("gross", new DateOnly(2027, 1, 1), Ct)).ShouldBe(0);
        (await store.CountOtherTradesAsync("cfg:none", new DateOnly(2027, 1, 1), Ct)).ShouldBe(2);
        (await store.ReadTradesAsync("cfg:none", Ct)).ShouldBeEmpty();
        (await store.ReadFormationsAsync("pairs:other", Ct)).ShouldBeEmpty();
        (await SqlStateAsync(server.App, "UPDATE research.pair_trades SET r = 0")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "DELETE FROM research.pair_trades")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "TRUNCATE research.pair_trades, research.pair_formations")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "UPDATE research.pair_formations SET selected = 0")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "DELETE FROM research.pair_formations")).ShouldBe("42501");
    }
}
