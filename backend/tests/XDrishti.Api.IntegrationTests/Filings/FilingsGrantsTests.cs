using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Domain.Filings;
using XDrishti.Infrastructure.FilingArchive;

namespace XDrishti.Api.IntegrationTests.Filings;

/// <summary>
/// P15-02 criterion 1 (ADR 0180): the five filing tables are insert-only for the runtime role <c>xd_app</c> (it can read and insert, and can neither update, delete nor truncate); the execution role and the read-only roles have no right on them; the whole path works
/// as <c>xd_app</c> (insert, find, <c>AsOf</c>, the supersede trigger). On the dedicated throw-away server of <see cref="ResearchGrantsTests"/>, migrated by the owner with the init script's default privileges.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class FilingsGrantsTests(ResearchGrantsTests.DedicatedServer server) : IClassFixture<ResearchGrantsTests.DedicatedServer>
{
    private static readonly string[] Tables = ["filings", "result_facts", "shareholding", "announcements", "calendar"];

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
    public async Task The_runtime_role_reads_and_inserts_and_can_neither_change_nor_delete_nor_truncate_a_filing_table()
    {
        foreach (var table in Tables)
        {
            foreach (var (privilege, expected) in new[] { ("SELECT", true), ("INSERT", true), ("UPDATE", false), ("DELETE", false), ("TRUNCATE", false) })
            {
                (await HasAsync("xd_app", "filings." + table, privilege)).ShouldBe(expected, table + " " + privilege);
            }
        }
    }

    [Fact]
    public async Task The_execution_and_the_read_only_roles_have_no_right_on_a_filing_table_and_no_other_table_of_the_schema_exists()
    {
        // The dedicated server has only xd_app. The other roles are created here (cluster-wide, no login) so the checks below are real: a missing role would answer false by default. The standing rules for these roles
        // on the whole database are enforced by ExecGrantsTests (xd_exec) and the read-only roles' tests; this test pins the filings tables.
        await StoreKit.ExecuteAsync(
            server.Owner,
            "DO $$ BEGIN IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_exec') THEN CREATE ROLE xd_exec NOLOGIN; END IF; IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_mcp_read') THEN CREATE ROLE xd_mcp_read NOLOGIN; END IF; IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_mcp_sql') THEN CREATE ROLE xd_mcp_sql NOLOGIN; END IF; END $$",
            Ct);
        foreach (var role in new[] { "xd_exec", "xd_mcp_read", "xd_mcp_sql" })
        {
            foreach (var table in Tables)
            {
                (await HasAsync(role, "filings." + table, "SELECT")).ShouldBeFalse(role + " " + table);
                (await HasAsync(role, "filings." + table, "INSERT")).ShouldBeFalse(role + " " + table);
            }
        }

        await using var connection = new NpgsqlConnection(server.Owner);
        await connection.OpenAsync(Ct);
        await using var list = new NpgsqlCommand("SELECT tablename FROM pg_tables WHERE schemaname = 'filings' ORDER BY 1", connection);
        var found = new List<string>();
        await using var reader = await list.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
        {
            found.Add(reader.GetString(0));
        }

        found.ShouldBe([.. Tables.Order(StringComparer.Ordinal)]); // no filings.extractions: that table waits for P15-08
    }

    [Fact]
    public async Task The_path_of_an_ingest_works_as_the_runtime_role_and_the_refused_statements_are_refused()
    {
        var instrument = Guid.NewGuid();
        await StoreKit.ExecuteAsync(
            server.Owner,
            $"INSERT INTO market.instruments (id, canonical_symbol, exchange, exchange_segment, type, name, display_name, lot_size, underlying_symbol, fno_eligible, first_seen_at, last_seen_at) VALUES ('{instrument}', 'NSE:GRANT', 'NSE', 'NSE_EQ', 'share', 'Grant Ltd', 'Grant Ltd', 1, 'GRANT', false, now(), now())",
            Ct);
        await using var dataSource = NpgsqlDataSource.Create(server.App);
        var store = new FilingStore(dataSource);
        var original = FilingTestKit.Ok(instrument, "grant-1", FilingTestKit.Q1, FilingTestKit.Broadcast);
        var restated = FilingTestKit.Ok(instrument, "grant-2", FilingTestKit.Q1, FilingTestKit.Broadcast.AddDays(30));

        var first = await store.InsertAsync(original, Ct);
        var second = await store.InsertAsync(restated, Ct);
        var again = await store.InsertAsync(original, Ct);
        var found = await store.FindByHashAsync(original.ContentSha256, Ct);
        var answer = await new PointInTimeFilings(dataSource, new FixedSettings()).AsOf(instrument, FilingKind.Results, FilingTestKit.Broadcast.AddDays(31), Ct);
        var instruments = await new FilingInstruments(dataSource).ResolveAsync("GRANT", Ct);
        var counts = await new FilingReader(dataSource).CoverageAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), Ct);

        (first.Outcome, second.Outcome, second.Supersedes, again.Outcome).ShouldBe((FilingInsertOutcome.Stored, FilingInsertOutcome.Stored, original.Id, FilingInsertOutcome.Duplicate));
        found.ShouldNotBeNull().Id.ShouldBe(original.Id);
        answer.Filings.ShouldHaveSingleItem().Id.ShouldBe(restated.Id);
        instruments.Id.ShouldBe(instrument);
        counts.Cells.ShouldHaveSingleItem().Superseded.ShouldBe(1);
        foreach (var table in Tables)
        {
            (await SqlStateAsync(server.App, $"UPDATE filings.{table} SET filing_id = filing_id".Replace("filing_id", table == "filings" ? "id" : "filing_id", StringComparison.Ordinal))).ShouldBe("42501");
            (await SqlStateAsync(server.App, $"DELETE FROM filings.{table}")).ShouldBe("42501");
            (await SqlStateAsync(server.App, $"TRUNCATE filings.{table}")).ShouldBe("42501");
        }
    }

    private sealed class FixedSettings : IFilingSettings
    {
        public int LagMinutes => 5;

        public long MaxFilesBytes => long.MaxValue;

        public long MinFreeBytes => 0;
    }
}
