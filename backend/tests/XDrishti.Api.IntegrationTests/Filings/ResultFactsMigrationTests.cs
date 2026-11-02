using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Filings;

/// <summary>P15-03 criterion 4: <c>ReshapeResultFacts</c> checks that the old fact table is empty, replaces it, creates the parse table, agrees with the model and goes back to the P15-02 shape only when both are empty.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class ResultFactsMigrationTests(ApiFactory factory)
{
    private const string Before = "20261102120000_AddExtensionTables";
    private const string Reshape = "20261102123646_ReshapeResultFacts";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<List<string>> ListAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var rows = new List<string>();
        while (await reader.ReadAsync(Ct))
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    private static async Task ExecAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }

    private static async Task SeedFilingAsync(NpgsqlConnection connection, Guid filing)
    {
        var instrument = Guid.NewGuid();
        await ExecAsync(connection, $"INSERT INTO market.instruments (id, canonical_symbol, exchange, exchange_segment, type, name, display_name, lot_size, underlying_symbol, fno_eligible, first_seen_at, last_seen_at) VALUES ('{instrument}', 'NSE:MIG', 'NSE', 'NSE_EQ', 'share', 'Mig Ltd', 'Mig Ltd', 1, 'MIG', false, now(), now())");
        await ExecAsync(
            connection,
            $"INSERT INTO filings.filings (id, instrument_id, kind, period_end, broadcast_ts, broadcast_state, unavailable_reason, ingested_at, source, content_sha256, file_bytes, file_name, supersedes) VALUES ('{filing}', '{instrument}', 'results', '2026-06-30', '2026-07-24T10:42:00Z', 'ok', NULL, now(), 'folder_drop', '{new string('a', 64)}', 1, 'a.xml', NULL)");
    }

    private static string Columns(string table) => $"SELECT column_name || '|' || data_type || '|' || is_nullable FROM information_schema.columns WHERE table_schema = 'filings' AND table_name = '{table}' ORDER BY ordinal_position";

    [Fact]
    public async Task Up_creates_both_tables_with_their_keys_and_checks_and_agrees_with_the_model()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_reshape_up_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);

        (await ListAsync(connection, Columns("result_facts"))).ShouldBe(
        [
            "filing_id|uuid|NO", "parser_version|character varying|NO", "field_code|character varying|NO", "span|character varying|NO", "basis|character varying|NO", "value|numeric|YES", "unit|character varying|NO", "state|character varying|NO",
            "unavailable_reason|character varying|YES",
        ]);
        (await ListAsync(connection, Columns("result_parses"))).ShouldBe(
        [
            "filing_id|uuid|NO", "parser_version|character varying|NO", "outcome|character varying|NO", "reason|character varying|YES", "facts|integer|NO", "flags|jsonb|NO", "parsed_at|timestamp with time zone|NO",
        ]);
        (await ListAsync(connection, "SELECT conname FROM pg_constraint WHERE conrelid = 'filings.result_facts'::regclass AND contype <> 'n' ORDER BY 1")).ShouldBe(
        [
            "ck_result_facts_basis", "ck_result_facts_reason", "ck_result_facts_span", "ck_result_facts_state", "ck_result_facts_value", "fk_result_facts_filing", "fk_result_facts_parse", "pk_result_facts",
        ]);
        (await ListAsync(connection, "SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = 'pk_result_facts'")).ShouldBe(["PRIMARY KEY (filing_id, parser_version, field_code, span, basis)"]);
        (await ListAsync(connection, "SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = 'ck_result_parses_outcome'")).Single().ShouldContain("'not_xbrl'");
        scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().Database.HasPendingModelChanges().ShouldBeFalse("the migration and the model snapshot agree");
    }

    [Fact]
    public async Task The_rows_are_checked_state_with_value_and_reason_with_unavailable_and_the_span_and_basis_are_closed()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_reshape_checks_probe", Ct);
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);
        var filing = Guid.NewGuid();
        await SeedFilingAsync(connection, filing);
        await ExecAsync(connection, $"INSERT INTO filings.result_parses (filing_id, parser_version, outcome, reason, facts, flags, parsed_at) VALUES ('{filing}', 'xbrl-1-synthetic', 'parsed', NULL, 1, '{{}}', now())");
        string Fact(string span, string basis, string value, string state, string reason) =>
            $"INSERT INTO filings.result_facts (filing_id, parser_version, field_code, span, basis, value, unit, state, unavailable_reason) VALUES ('{filing}', 'xbrl-1-synthetic', 'revenue', '{span}', '{basis}', {value}, 'inr', '{state}', {reason})";

        await ExecAsync(connection, Fact("q", "consolidated", "5", "value", "NULL"));
        await ExecAsync(connection, Fact("q", "standalone", "NULL", "unavailable", "'not_in_file'"));

        foreach (var bad in new[] { Fact("q", "consolidated", "5", "value", "NULL"), Fact("x", "standalone", "5", "value", "NULL"), Fact("q", "group", "5", "value", "NULL"), Fact("ytd", "standalone", "NULL", "value", "NULL"), Fact("ytd", "standalone", "5", "unavailable", "'x'"), Fact("ytd", "standalone", "NULL", "unavailable", "NULL"), Fact("ytd", "standalone", "5", "value", "'why'") })
        {
            await Should.ThrowAsync<PostgresException>(async () => await ExecAsync(connection, bad));
        }

        await Should.ThrowAsync<PostgresException>(async () => await ExecAsync(connection, $"INSERT INTO filings.result_facts (filing_id, parser_version, field_code, span, basis, value, unit, state, unavailable_reason) VALUES ('{filing}', 'other', 'revenue', 'q', 'consolidated', 1, 'inr', 'value', NULL)"), "a fact needs the parse row of its version");
        await Should.ThrowAsync<PostgresException>(async () => await ExecAsync(connection, $"INSERT INTO filings.result_parses (filing_id, parser_version, outcome, reason, facts, flags, parsed_at) VALUES ('{filing}', 'v2', 'rejected', 'not_xml', 3, '{{}}', now())"), "a rejected parse has no fact");
    }

    [Fact]
    public async Task Up_refuses_and_changes_nothing_when_the_old_table_has_a_row_then_goes_through_when_it_is_empty()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_reshape_refuse_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().GetService<IMigrator>();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);
        await migrator.MigrateAsync(Before, Ct);
        var filing = Guid.NewGuid();
        await SeedFilingAsync(connection, filing);
        await ExecAsync(connection, $"INSERT INTO filings.result_facts (filing_id, field_code, value, unit, basis, state) VALUES ('{filing}', 'revenue', 1, 'inr', 'consolidated', 'value')");

        var refused = await Should.ThrowAsync<Exception>(async () => await migrator.MigrateAsync(Reshape, Ct));

        refused.ToString().ShouldContain("ReshapeResultFacts changes nothing");
        (await ListAsync(connection, Columns("result_facts"))).Count.ShouldBe(6, "the old table is still there");
        (await ListAsync(connection, "SELECT count(*)::text FROM filings.result_facts")).ShouldBe(["1"]);
        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'filings' AND table_name = 'result_parses'")).ShouldBeEmpty();

        await ExecAsync(connection, "DELETE FROM filings.result_facts");
        await migrator.MigrateAsync(Reshape, Ct);

        (await ListAsync(connection, Columns("result_facts"))).Count.ShouldBe(9);
    }

    [Fact]
    public async Task Down_refuses_with_a_parse_row_and_restores_the_P15_02_shape_when_empty_and_up_again_is_clean()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_reshape_down_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().GetService<IMigrator>();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);
        var filing = Guid.NewGuid();
        await SeedFilingAsync(connection, filing);
        await ExecAsync(connection, $"INSERT INTO filings.result_parses (filing_id, parser_version, outcome, reason, facts, flags, parsed_at) VALUES ('{filing}', 'xbrl-1-synthetic', 'not_xbrl', NULL, 0, '{{}}', now())");

        var refused = await Should.ThrowAsync<Exception>(async () => await migrator.MigrateAsync(Before, Ct));

        refused.ToString().ShouldContain("changes nothing");
        (await ListAsync(connection, Columns("result_parses"))).Count.ShouldBe(7);

        await ExecAsync(connection, "DELETE FROM filings.result_parses");
        await migrator.MigrateAsync(Before, Ct);

        (await ListAsync(connection, Columns("result_facts"))).ShouldBe(["filing_id|uuid|NO", "field_code|character varying|NO", "value|numeric|YES", "unit|character varying|NO", "basis|character varying|NO", "state|character varying|NO"]);
        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'filings' ORDER BY 1")).ShouldBe(["announcements", "calendar", "event_filter_decisions", "filings", "result_facts", "shareholding"]);

        await migrator.MigrateAsync(null, Ct);

        (await ListAsync(connection, Columns("result_facts"))).Count.ShouldBe(9);
        (await ListAsync(connection, Columns("result_parses"))).Count.ShouldBe(7);
    }
}
