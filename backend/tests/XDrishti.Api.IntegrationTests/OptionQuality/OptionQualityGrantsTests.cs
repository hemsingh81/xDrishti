using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;
using XDrishti.Application.Abstractions.OptionQuality;
using XDrishti.Domain.Options.Quality;
using XDrishti.Infrastructure.OptionQuality;

namespace XDrishti.Api.IntegrationTests.OptionQuality;

/// <summary>
/// P14-04 criterion 9 (ADR 0178): the two result tables are insert-only for the runtime role <c>xd_app</c> (it can read and insert, and can neither update, delete nor truncate); the execution role and the read-only roles have no right on them; the runtime role
/// may read every archive table the quality checks read, and nothing more; the whole path works as <c>xd_app</c>: the run id from the sequence, the copy of results, the run row, the reads and the reader on the archive. On the dedicated throw-away server of
/// <see cref="ResearchGrantsTests"/>, migrated by the owner with the init script's default privileges.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class OptionQualityGrantsTests(ResearchGrantsTests.DedicatedServer server) : IClassFixture<ResearchGrantsTests.DedicatedServer>
{
    private static readonly string[] Tables = ["quality_runs", "quality_results"];

    private static readonly string[] Read = ["options.chain_snapshots", "options.chain_requests", "options.chain_sessions", "options.rolling_bars", "options.fetch_log", "options.contracts", "options.lot_sizes", "options.market_epochs", "market.instruments", "market.bars_1m"];

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

    [Fact]
    public async Task The_runtime_role_reads_and_inserts_and_can_neither_change_nor_delete_nor_truncate_a_result_table()
    {
        foreach (var table in Tables)
        {
            foreach (var (privilege, expected) in new[] { ("SELECT", true), ("INSERT", true), ("UPDATE", false), ("DELETE", false), ("TRUNCATE", false) })
            {
                (await HasAsync("xd_app", "options." + table, privilege)).ShouldBe(expected, table + " " + privilege);
            }
        }
    }

    [Fact]
    public async Task The_execution_and_the_read_only_roles_have_no_right_on_a_result_table()
    {
        foreach (var role in new[] { "xd_exec", "xd_mcp_read", "xd_mcp_sql" })
        {
            foreach (var table in Tables)
            {
                (await HasAsync(role, "options." + table, "SELECT")).ShouldBeFalse(role + " " + table);
                (await HasAsync(role, "options." + table, "INSERT")).ShouldBeFalse(role + " " + table);
            }
        }
    }

    [Fact]
    public async Task The_runtime_role_can_read_every_table_the_checks_read_and_write_none_of_the_archive_ones()
    {
        foreach (var table in Read)
        {
            (await HasAsync("xd_app", table, "SELECT")).ShouldBeTrue(table);
        }

        foreach (var table in Read.Where(t => t.StartsWith("options.chain_", StringComparison.Ordinal) || t is "options.rolling_bars" or "options.fetch_log"))
        {
            (await HasAsync("xd_app", table, "UPDATE")).ShouldBeFalse(table);
            (await HasAsync("xd_app", table, "DELETE")).ShouldBeFalse(table);
        }
    }

    [Fact]
    public async Task The_path_of_a_run_works_as_the_runtime_role_and_the_refused_statements_are_refused()
    {
        await using var dataSource = NpgsqlDataSource.Create(server.App);
        var store = new OptionQualityStore(dataSource);
        var underlying = Guid.NewGuid();
        var day = new DateOnly(2026, 10, 9);
        var started = new DateTimeOffset(2026, 10, 12, 10, 45, 0, TimeSpan.Zero);

        var run = await store.StartRunAsync(Ct);
        await store.AddResultsAsync(run, [
            new OptionQualityRow(day, underlying, "chain", null, null, new QualityCheck("coverage", QualityStatus.Pass, 0.97, 0.95, null, 100, "{\"ok\":97,\"timeout\":3}")),
            new OptionQualityRow(day, underlying, "chain", null, null, QualityCheck.Unavailable("parity", "no_two_sided_quote")),
            new OptionQualityRow(day, underlying, "rolling", null, "key-1", new QualityCheck("stitching", QualityStatus.Warn, 0.004, 0.01, null, 375, null)),
        ], Ct);
        await store.FinishRunAsync(run, "daily", started, started.AddMinutes(1), 1, 3, null, Ct);
        var chain = await store.ChainResultsAsync(underlying, day, Ct);
        var rolling = await store.RollingResultsAsync(underlying, day, day, 10, Ct);
        var reader = new OptionArchiveReader(dataSource);

        chain.Select(r => (r.Check.Code, r.Check.Status, r.Check.Reason)).ShouldBe([("coverage", QualityStatus.Pass, null), ("parity", QualityStatus.Unavailable, "no_two_sided_quote")]);
        chain[0].Check.Detail.ShouldNotBeNull().ShouldContain("\"ok\": 97");
        rolling.ShouldHaveSingleItem().FetchKey.ShouldBe("key-1");
        (await reader.PendingChainSessionsAsync(day, 10, Ct)).ShouldBeEmpty();
        (await reader.PendingRollingWindowsAsync(day, 10, Ct)).ShouldBeEmpty();
        (await reader.ChainPointsAsync(underlying, day, Ct)).ShouldBeEmpty();
        (await reader.FirstChainSessionAsync(Ct)).ShouldBeNull();
        (await SqlStateAsync(server.App, "UPDATE options.quality_results SET status = 'pass'")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "DELETE FROM options.quality_results")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "TRUNCATE options.quality_results")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "UPDATE options.quality_runs SET results = 0")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "DELETE FROM options.quality_runs")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "INSERT INTO options.chain_sessions (session_date, started_at, ended_at, end_reason, slots, requests, ok, gaps, rows) VALUES ('2026-10-09', now(), now(), 'window_end', 0, 0, 0, 0, 0)")).ShouldBeNull();
        (await SqlStateAsync(server.App, "DELETE FROM options.chain_sessions")).ShouldBe("42501");
    }

    [Fact]
    public async Task The_database_refuses_a_result_that_is_unavailable_without_a_reason_a_rolling_result_without_a_key_and_a_word_outside_the_lists()
    {
        const string Insert = "INSERT INTO options.quality_results (run_id, session_date, underlying_id, source, fetch_key, check_code, status, reason, n) VALUES (1, '2026-10-09', gen_random_uuid(), ";

        (await SqlStateAsync(server.App, Insert + "'chain', NULL, 'coverage', 'unavailable', NULL, 0)")).ShouldBe("23514");
        (await SqlStateAsync(server.App, Insert + "'rolling', NULL, 'coverage', 'pass', NULL, 0)")).ShouldBe("23514");
        (await SqlStateAsync(server.App, Insert + "'chain', 'k', 'coverage', 'pass', NULL, 0)")).ShouldBe("23514");
        (await SqlStateAsync(server.App, Insert + "'chain', NULL, 'coverage', 'good', NULL, 0)")).ShouldBe("23514");
        (await SqlStateAsync(server.App, Insert + "'chain', NULL, 'iv_smile', 'pass', NULL, 0)")).ShouldBe("23514");
        (await SqlStateAsync(server.App, Insert + "'chain', NULL, 'coverage', 'unavailable', 'because', 0)")).ShouldBe("23514");
        (await SqlStateAsync(server.App, Insert + "'chain', NULL, 'coverage', 'unavailable', 'not_recorded', 0)")).ShouldBeNull();
    }
}
