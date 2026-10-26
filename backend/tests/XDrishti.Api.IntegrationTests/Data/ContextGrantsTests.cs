using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;

namespace XDrishti.Api.IntegrationTests.Data;

/// <summary>
/// P3-01d criterion 3 and P3-01e (column grants): <c>market.context_daily</c> is insert-only for the runtime role <c>xd_app</c>. Runs on the dedicated throw-away server of <see cref="ResearchGrantsTests"/> (the role has the default privileges of the init script, the chain is migrated by the
/// owner). The assertion is on the table-level privileges: a later market migration that re-grants the schema and forgets <c>MakeAppendOnlyForAppRole</c> fails here, and so does one that adds a table-level UPDATE or DELETE. Column grants (P3-01e) are not table-level.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ContextGrantsTests(ResearchGrantsTests.DedicatedServer server) : IClassFixture<ResearchGrantsTests.DedicatedServer>
{
    private const string Insert = """
        INSERT INTO market.context_daily (date, index_instrument_id, index_high, index_low, index_close, vix_close, index_bars, vix_bars, source, written_at)
        VALUES ('2026-03-02', gen_random_uuid(), 22100, 21900, 22000, 15, 375, 375, 'derived_1m', now())
        """;

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
    public async Task The_runtime_role_reads_and_inserts_and_can_neither_change_nor_delete_nor_truncate_and_a_stored_date_is_not_replaced()
    {
        await using var connection = new NpgsqlConnection(server.Owner);
        await connection.OpenAsync(Ct);
        foreach (var (privilege, expected) in new[] { ("SELECT", true), ("INSERT", true), ("UPDATE", false), ("DELETE", false), ("TRUNCATE", false) })
        {
            await using var check = new NpgsqlCommand("SELECT has_table_privilege('xd_app', 'market.context_daily', @p)", connection);
            check.Parameters.AddWithValue("p", privilege);
            ((bool)(await check.ExecuteScalarAsync(Ct))!).ShouldBe(expected, privilege);
        }

        (await SqlStateAsync(server.App, Insert)).ShouldBeNull();
        (await SqlStateAsync(server.App, Insert + " ON CONFLICT (date) DO NOTHING")).ShouldBeNull("a stored date is left alone");
        (await SqlStateAsync(server.App, Insert)).ShouldBe("23505", "a plain second insert of the same date is a key violation");
        (await SqlStateAsync(server.App, "UPDATE market.context_daily SET index_close = 1")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "DELETE FROM market.context_daily")).ShouldBe("42501");
        (await SqlStateAsync(server.App, Insert.Replace("'2026-03-02'", "'2026-03-03'").Replace("22000, 15", "99999, 15"))).ShouldBe("23514", "the close must lie between the low and the high");
        (await SqlStateAsync(server.App, Insert.Replace("'2026-03-02'", "'2026-03-04'").Replace("22000, 15", "22000, -1"))).ShouldBe("23514", "the VIX must be positive");
    }

    [Fact]
    public async Task The_runtime_role_may_update_the_six_breadth_columns_and_no_other_column_and_the_checks_hold()
    {
        await using var connection = new NpgsqlConnection(server.Owner);
        await connection.OpenAsync(Ct);
        var breadth = new[] { "breadth", "breadth_voters", "breadth_universe", "breadth_excluded", "breadth_state", "breadth_computed_at" };
        var all = new List<string>();
        await using (var columns = new NpgsqlCommand("SELECT column_name FROM information_schema.columns WHERE table_schema = 'market' AND table_name = 'context_daily'", connection))
        await using (var reader = await columns.ExecuteReaderAsync(Ct))
        {
            while (await reader.ReadAsync(Ct))
            {
                all.Add(reader.GetString(0));
            }
        }

        breadth.ShouldBeSubsetOf(all);
        foreach (var column in all)
        {
            await using var check = new NpgsqlCommand("SELECT has_column_privilege('xd_app', 'market.context_daily', @c, 'UPDATE')", connection);
            check.Parameters.AddWithValue("c", column);
            ((bool)(await check.ExecuteScalarAsync(Ct))!).ShouldBe(breadth.Contains(column), column);
        }

        await using (var table = new NpgsqlCommand("SELECT has_table_privilege('xd_app', 'market.context_daily', 'UPDATE')", connection))
        {
            ((bool)(await table.ExecuteScalarAsync(Ct))!).ShouldBeFalse("a column privilege is not a table privilege");
        }

        (await SqlStateAsync(server.App, Insert.Replace("'2026-03-02'", "'2026-03-09'"))).ShouldBeNull();
        (await SqlStateAsync(server.App, "UPDATE market.context_daily SET breadth = 55, breadth_voters = 60, breadth_universe = 90, breadth_excluded = 30, breadth_state = 'value', breadth_computed_at = now() WHERE date = '2026-03-09' AND breadth_state IS NULL")).ShouldBeNull();
        (await SqlStateAsync(server.App, "UPDATE market.context_daily SET index_close = 1 WHERE date = '2026-03-09'")).ShouldBe("42501", "the index figures stay insert-only");
        (await SqlStateAsync(server.App, "UPDATE market.context_daily SET vix_close = 1, breadth = 1 WHERE date = '2026-03-09'")).ShouldBe("42501", "one refused column refuses the statement");
        (await SqlStateAsync(server.App, "UPDATE market.context_daily SET breadth = 101 WHERE date = '2026-03-09'")).ShouldBe("23514");
        (await SqlStateAsync(server.App, "UPDATE market.context_daily SET breadth_state = 'maybe' WHERE date = '2026-03-09'")).ShouldBe("23514");
        (await SqlStateAsync(server.App, "DELETE FROM market.context_daily WHERE date = '2026-03-09'")).ShouldBe("42501");
    }
}
