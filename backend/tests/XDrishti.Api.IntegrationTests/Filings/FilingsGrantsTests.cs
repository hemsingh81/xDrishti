using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Abstractions.Selection;
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
    private static readonly string[] Tables = ["filings", "result_facts", "shareholding", "announcements", "calendar", "event_filter_decisions", "result_parses"];   // event_filter_decisions is the event-risk switch history (P15-06), result_parses the parse outcomes (P15-03)

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

        found.ShouldBe([.. Tables.Order(StringComparer.Ordinal)]); // no filings.extractions: that table waits for P15-08; event_filter_decisions is P15-06
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
        counts.Cells.Single(c => c.Kind == "results").Superseded.ShouldBe(1); // the server is shared with the P15-04 test below, which stores other kinds
        foreach (var table in Tables)
        {
            (await SqlStateAsync(server.App, $"UPDATE filings.{table} SET filing_id = filing_id".Replace("filing_id", table is "filings" or "event_filter_decisions" ? "id" : "filing_id", StringComparison.Ordinal))).ShouldBe("42501");
            (await SqlStateAsync(server.App, $"DELETE FROM filings.{table}")).ShouldBe("42501");
            (await SqlStateAsync(server.App, $"TRUNCATE filings.{table}")).ShouldBe("42501");
        }
    }

    [Fact]
    public async Task The_calendar_and_shareholding_stores_work_as_the_runtime_role_and_read_back_through_the_ports()
    {
        var instrument = Guid.NewGuid();
        await StoreKit.ExecuteAsync(
            server.Owner,
            $"INSERT INTO market.instruments (id, canonical_symbol, exchange, exchange_segment, type, name, display_name, lot_size, underlying_symbol, fno_eligible, first_seen_at, last_seen_at) VALUES ('{instrument}', 'NSE:GRANTB', 'NSE', 'NSE_EQ', 'share', 'Grant B Ltd', 'Grant B Ltd', 1, 'GRANTB', false, now(), now())",
            Ct);
        await using var dataSource = NpgsqlDataSource.Create(server.App);
        var ts = new DateTimeOffset(2026, 8, 3, 5, 30, 0, TimeSpan.Zero);

        var meeting = await new CalendarStore(dataSource).InsertAsync(CalendarTestKit.Meeting(instrument, "grant-m", new DateOnly(2026, 8, 12), ts), Ct);
        var holding = await new ShareholdingStore(dataSource).InsertAsync(CalendarTestKit.Holding(instrument, "grant-h", CalendarTestKit.Q1, ts, 50m), Ct);
        var rows = await new CalendarRows(dataSource).ResultsRowsAsync([instrument], ts.AddDays(1), Ct);
        var groups = await new ShareholdingRows(dataSource).GroupsAsync(instrument, ts.AddDays(1), Ct);
        var tracked = await new FilingInstruments(dataSource).TrackedAsync(Ct);

        (meeting.Outcome, holding.Outcome).ShouldBe((FilingInsertOutcome.Stored, FilingInsertOutcome.Stored));
        rows.ShouldHaveSingleItem().MeetingDate.ShouldBe(new DateOnly(2026, 8, 12));
        groups.ShouldHaveSingleItem().Lines.Count.ShouldBe(3);
        tracked.ShouldContain(t => t.Id == instrument && t.Symbol == "GRANTB");
        (await SqlStateAsync(server.App, "UPDATE filings.calendar SET status = 'cancelled'")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "UPDATE filings.shareholding SET pct = 1")).ShouldBe("42501");
    }

    [Fact]
    public async Task The_result_parse_store_works_as_the_runtime_role_and_the_refused_statements_are_refused()
    {
        var instrument = Guid.NewGuid();
        await StoreKit.ExecuteAsync(
            server.Owner,
            $"INSERT INTO market.instruments (id, canonical_symbol, exchange, exchange_segment, type, name, display_name, lot_size, underlying_symbol, fno_eligible, first_seen_at, last_seen_at) VALUES ('{instrument}', 'NSE:GRANTC', 'NSE', 'NSE_EQ', 'share', 'Grant C Ltd', 'Grant C Ltd', 1, 'GRANTC', false, now(), now())",
            Ct);
        await using var dataSource = NpgsqlDataSource.Create(server.App);
        var filing = FilingTestKit.Ok(instrument, "grant-parse", FilingTestKit.Q1, FilingTestKit.Broadcast);
        await new FilingStore(dataSource).InsertAsync(filing, Ct);
        var store = new ResultFactsStore(dataSource);
        var parsed = new ParsedResults("parsed", null, [ParsedFact.Of("revenue", "q", "consolidated", 12.50m), ParsedFact.Missing("pat", "q", "consolidated", "not_in_file")], new Dictionary<string, int> { ["unknown_span"] = 1 });

        var pending = await store.PendingAsync("xbrl-1-synthetic", 10, Ct);
        var saved = await store.SaveAsync(filing.Id, "xbrl-1-synthetic", parsed, FilingTestKit.Ingested, Ct);
        var again = await store.SaveAsync(filing.Id, "xbrl-1-synthetic", parsed, FilingTestKit.Ingested, Ct);
        var rows = new ResultFactRows(dataSource);
        var parse = await rows.ParseOfAsync(filing.Id, "xbrl-1-synthetic", Ct);
        var facts = await rows.FactsOfAsync(filing.Id, "xbrl-1-synthetic", Ct);
        var answer = ResultFactsAsOfRule.Compute(await rows.ForInstrumentAsync(instrument, FilingTestKit.Broadcast.AddDays(1), "xbrl-1-synthetic", Ct), FilingTestKit.Broadcast.AddDays(1), TimeSpan.FromMinutes(5));

        pending.ShouldContain(p => p.Id == filing.Id);
        (saved, again).ShouldBe((true, false));
        (parse.ShouldNotBeNull().Outcome, parse.Facts, parse.Flags["unknown_span"]).ShouldBe(("parsed", 2, 1));
        facts.Select(f => (f.Field, f.Value, f.Reason)).ShouldBe([("revenue", 12.50m, null), ("pat", null, "not_in_file")]);
        answer.Get(FilingTestKit.Q1, "revenue", "q", "consolidated").Value.ShouldBe(12.50m);
        (await SqlStateAsync(server.App, "UPDATE filings.result_parses SET facts = 0")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "DELETE FROM filings.result_facts")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "TRUNCATE filings.result_parses")).ShouldBe("42501");
    }

    [Fact]
    public async Task The_event_filter_history_works_as_the_runtime_role_through_the_sequence_and_the_trigger_and_the_refused_statements_are_refused()
    {
        await using var dataSource = NpgsqlDataSource.Create(server.App);
        var decisions = new EventFilterDecisions(dataSource);

        var owner = await decisions.AppendAsync(new NewEventFilterRow(false, 2, "auto", "owner", "owner:grant@example.invalid", "arm the auto option", "{}", null), Ct);
        var auto = await decisions.AppendAsync(new NewEventFilterRow(true, 2, "auto", "auto", "auto:event-filter", "auto: keep", "{\"verdict.state\":\"KEEP\",\"verdict.hold_out_confirmed\":true,\"verdict.results_within_days\":2}", "pf-1"), Ct);
        var again = await decisions.AppendAsync(new NewEventFilterRow(true, 2, "auto", "auto", "auto:event-filter", "auto: keep", "{\"verdict.state\":\"KEEP\",\"verdict.hold_out_confirmed\":true,\"verdict.results_within_days\":2}", "pf-1"), Ct);
        var rows = await decisions.RowsAsync(10, Ct);

        (owner.Id, owner.Refused).ShouldBe((owner.Id, null));
        owner.Id.ShouldNotBeNull();
        auto.Id.ShouldBe(owner.Id + 1, "the identity sequence is granted to the runtime role");
        again.Id.ShouldBeNull();
        again.Refused.ShouldNotBeNull().ShouldContain("already on");
        rows.Select(r => r.DecidedBy).ShouldBe(["auto", "owner"]);
        (await SqlStateAsync(server.App, "UPDATE filings.event_filter_decisions SET enabled = false")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "DELETE FROM filings.event_filter_decisions")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "TRUNCATE filings.event_filter_decisions")).ShouldBe("42501");
        (await HasAsync("xd_app", "filings.event_filter_decisions", "INSERT")).ShouldBeTrue();
        (await ScalarBooleanAsync("SELECT has_sequence_privilege('xd_app', 'filings.event_filter_decisions_id_seq', 'USAGE')")).ShouldBeTrue();
        foreach (var role in new[] { "xd_exec", "xd_mcp_read", "xd_mcp_sql" })
        {
            (await ScalarBooleanAsync($"SELECT CASE WHEN EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{role}') THEN has_sequence_privilege('{role}', 'filings.event_filter_decisions_id_seq', 'USAGE') ELSE false END")).ShouldBeFalse(role);
        }
    }

    private async Task<bool> ScalarBooleanAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(server.Owner);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        return (bool)(await command.ExecuteScalarAsync(Ct))!;
    }

    private sealed class FixedSettings : IFilingSettings
    {
        public int LagMinutes => 5;

        public long MaxFilesBytes => long.MaxValue;

        public long MinFreeBytes => 0;
    }
}
