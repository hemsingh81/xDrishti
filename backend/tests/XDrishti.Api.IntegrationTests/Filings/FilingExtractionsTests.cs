using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Domain.Filings;
using XDrishti.Domain.Filings.Extraction;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Filings;

/// <summary>
/// P15-08 criterion 4 and the coverage route: <c>AddFilingExtractions</c> creates the table with its key and checks (state ok if and only if the answer is there, closed words, insert shape), agrees with the model, and goes back; the store lists what is pending, inserts once per
/// filing, model, prompt and schema, and counts; the coverage route needs a session and carries counts only.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class FilingExtractionsTests(ApiFactory factory) : DataTestBase(factory)
{
    private const string Before = "20261102210031_AddEventExitExitReason";
    private const string Migration = "20261102234713_AddFilingExtractions";

    private static readonly ExtractionKey Key = new(new string('a', 64), "announcement-flag", "v1", new string('b', 64), "1");

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

    private static string Insert(Guid filing, string state, string? reason, string? material, string? category, string? model = null, string? rationale = "'a reason'", string guidance = "'none'", string rating = "'none'", string order = "NULL") =>
        $"INSERT INTO filings.extractions (filing_id, model_sha256, prompt_name, prompt_version, prompt_hash, schema_version, state, reason, material, category, rationale, order_value_inr, guidance, rating_action, ungrounded, latency_ms, created_at) VALUES ('{filing}', '{model ?? new string('a', 64)}', 'announcement-flag', 'v1', '{new string('b', 64)}', '1', '{state}', {(reason is null ? "NULL" : $"'{reason}'")}, {material ?? "NULL"}, {(category is null ? "NULL" : $"'{category}'")}, {(state == "ok" ? rationale : "NULL")}, {order}, {(state == "ok" ? guidance : "NULL")}, {(state == "ok" ? rating : "NULL")}, false, 10, now())";

    private static async Task<Guid> SeedFilingAsync(NpgsqlConnection connection)
    {
        var instrument = Guid.NewGuid();
        var filing = Guid.NewGuid();
        await ExecAsync(connection, $"INSERT INTO market.instruments (id, canonical_symbol, exchange, exchange_segment, type, name, display_name, lot_size, underlying_symbol, fno_eligible, first_seen_at, last_seen_at) VALUES ('{instrument}', 'NSE:EXT{filing.ToString("N")[..6]}', 'NSE', 'NSE_EQ', 'share', 'Ext Ltd', 'Ext Ltd', 1, 'EXT', false, now(), now())");
        await ExecAsync(
            connection,
            $"INSERT INTO filings.filings (id, instrument_id, kind, period_end, broadcast_ts, broadcast_state, unavailable_reason, ingested_at, source, content_sha256, file_bytes, file_name, supersedes) VALUES ('{filing}', '{instrument}', 'announcement', NULL, '2026-07-24T10:42:00Z', 'ok', NULL, now(), 'folder_drop', '{filing.ToString("N")}{filing.ToString("N")}', 1, 'a.json', NULL)");
        return filing;
    }

    [Fact]
    public async Task The_migration_creates_the_table_with_its_key_and_checks_agrees_with_the_model_and_down_drops_it()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(Factory, "xd_extractions_probe", Ct, migrate: false);
        using var scope = scratch.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>();
        var migrator = db.GetService<IMigrator>();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);
        await migrator.MigrateAsync(Before, Ct);
        (await ListAsync(connection, "SELECT tablename FROM pg_tables WHERE schemaname = 'filings' AND tablename = 'extractions'")).ShouldBeEmpty();

        await migrator.MigrateAsync(Migration, Ct);

        (await ListAsync(connection, "SELECT column_name || '|' || data_type || '|' || is_nullable FROM information_schema.columns WHERE table_schema = 'filings' AND table_name = 'extractions' ORDER BY ordinal_position")).ShouldBe(
        [
            "filing_id|uuid|NO", "model_sha256|character|NO", "prompt_name|character varying|NO", "prompt_version|character varying|NO", "prompt_hash|character|NO", "schema_version|character varying|NO", "state|character varying|NO", "reason|character varying|YES",
            "material|boolean|YES", "category|character varying|YES", "rationale|character varying|YES", "order_value_inr|numeric|YES", "guidance|character varying|YES", "rating_action|character varying|YES", "ungrounded|boolean|NO", "latency_ms|integer|NO",
            "prompt_tokens|integer|YES", "completion_tokens|integer|YES", "created_at|timestamp with time zone|NO",
        ]);
        (await ListAsync(connection, "SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = 'pk_extractions'")).ShouldBe(["PRIMARY KEY (filing_id, model_sha256, prompt_hash, schema_version)"]);
        (await ListAsync(connection, "SELECT conname FROM pg_constraint WHERE conrelid = 'filings.extractions'::regclass AND contype <> 'n' ORDER BY 1")).ShouldBe(
        [
            "ck_extractions_category", "ck_extractions_counts", "ck_extractions_empty_unless_ok", "ck_extractions_guidance", "ck_extractions_model", "ck_extractions_ok_answer", "ck_extractions_ok_fields", "ck_extractions_prompt", "ck_extractions_rating",
            "ck_extractions_reason", "ck_extractions_state", "fk_extractions_filing", "pk_extractions",
        ]);
        db.Database.HasPendingModelChanges().ShouldBeFalse("the migration and the model snapshot agree");

        await migrator.MigrateAsync(Before, Ct);

        (await ListAsync(connection, "SELECT tablename FROM pg_tables WHERE schemaname = 'filings' AND tablename = 'extractions'")).ShouldBeEmpty();
        (await ListAsync(connection, "SELECT tablename FROM pg_tables WHERE schemaname = 'filings' ORDER BY 1")).ShouldBe(["announcements", "calendar", "event_filter_decisions", "filings", "result_facts", "result_parses", "shareholding"]);
    }

    [Fact]
    public async Task The_checks_make_state_ok_equal_to_the_whole_answer_and_every_other_state_hold_a_reason_and_none_of_it()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(Factory, "xd_extractions_checks_probe", Ct);
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);
        var filing = await SeedFilingAsync(connection);

        await ExecAsync(connection, Insert(filing, "ok", null, "true", "order_win", order: "45000000"));
        await ExecAsync(connection, Insert(filing, "invalid_output", "bad_keys", null, null, model: new string('c', 64)));
        await ExecAsync(connection, Insert(filing, "model_unavailable", "server_failed", null, null, model: new string('d', 64)));
        await ExecAsync(connection, Insert(filing, "too_long", "text_too_long", null, null, model: new string('e', 64)));

        var bad = new[]
        {
            Insert(filing, "ok", null, "true", "order_win"),                                             // the same key twice
            Insert(filing, "ok", null, null, "order_win", model: new string('1', 64)),                  // ok without material
            Insert(filing, "ok", null, "true", null, model: new string('2', 64)),                       // ok without category
            Insert(filing, "ok", "why", "true", "order_win", model: new string('3', 64)),               // ok with a reason
            Insert(filing, "ok", null, "true", "nonsense", model: new string('4', 64)),                 // a category outside the list
            Insert(filing, "ok", null, "true", "other", model: new string('5', 64), guidance: "'up'"),  // guidance outside the list
            Insert(filing, "ok", null, "true", "other", model: new string('6', 64), rating: "'x'"),     // rating outside the list
            Insert(filing, "ok", null, "true", "other", model: new string('7', 64), rationale: "NULL"), // ok without a rationale
            Insert(filing, "invalid_output", null, null, null, model: new string('8', 64)),             // a refusal without a reason
            Insert(filing, "invalid_output", "bad_keys", "true", null, model: new string('9', 64)),     // a refusal holding a flag
            Insert(filing, "invalid_output", "bad_keys", null, null, model: new string('f', 64)).Replace("NULL, NULL, false", "NULL, 5, false", StringComparison.Ordinal), // a refusal holding an order value
            Insert(filing, "guessed", "x", null, null, model: new string('0', 64)),                     // a state outside the list
            Insert(filing, "ok", null, "true", "other", model: new string('A', 64)),                    // upper-case hash
            Insert(Guid.NewGuid(), "ok", null, "true", "other", model: new string('7', 64)),            // a filing that does not exist
        };
        foreach (var statement in bad)
        {
            await Should.ThrowAsync<PostgresException>(async () => await ExecAsync(connection, statement), statement[..Math.Min(statement.Length, 160)]);
        }

        (await ListAsync(connection, "SELECT count(*)::text FROM filings.extractions")).ShouldBe(["4"]);
    }

    [Fact]
    public async Task The_store_lists_what_is_pending_for_a_key_inserts_once_and_counts_per_state()
    {
        var instrument = await SeedAsync("EXTA");
        using var scope = Factory.Services.CreateScope();
        var announcements = scope.ServiceProvider.GetRequiredService<IAnnouncementStore>();
        var store = scope.ServiceProvider.GetRequiredService<IAnnouncementExtractionStore>();
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var filing = FilingTestKit.Ok(instrument, "ext-" + i, null, new DateTimeOffset(2026, 7, 24, 10, 0, 0, TimeSpan.Zero).AddMinutes(i), FilingKind.Announcement);
            (await announcements.InsertAsync(new NewAnnouncement(filing, "Order", "Headline " + i), Ct)).Outcome.ShouldBe(FilingInsertOutcome.Stored);
            ids.Add(filing.Id);
        }

        var from = new DateTimeOffset(2026, 7, 24, 0, 0, 0, TimeSpan.Zero);
        var pending = await store.PendingAsync(from, from.AddDays(1), Key, 0, 10, Ct);
        var first = await store.InsertAsync(new NewExtraction(ids[0], Key, "ok", null, new AnnouncementAnswer(true, "order_win", "r", 450m, "none", "none"), false, 25, 100, 20, FilingTestKit.Ingested), Ct);
        var again = await store.InsertAsync(new NewExtraction(ids[0], Key, "invalid_output", "bad_keys", null, false, 25, null, null, FilingTestKit.Ingested), Ct);
        var refusal = await store.InsertAsync(new NewExtraction(ids[1], Key, "invalid_output", "bad_keys", null, false, 25, null, null, FilingTestKit.Ingested), Ct);
        var dropped = await store.InsertAsync(new NewExtraction(ids[2], Key with { ModelSha256 = new string('c', 64) }, "ok", null, new AnnouncementAnswer(false, "other", "r", null, "none", "none"), true, 25, null, null, FilingTestKit.Ingested), Ct);

        pending.ShouldBe(ids);
        (first, again, refusal, dropped).ShouldBe((true, false, true, true));
        (await store.PendingAsync(from, from.AddDays(1), Key, 0, 10, Ct)).ShouldBe([ids[2]]);
        (await store.PendingAsync(from, from.AddDays(1), Key with { PromptHash = new string('9', 64) }, 1, 1, Ct)).ShouldBe([ids[1]]);
        (await store.PendingAsync(from.AddDays(1), from.AddDays(2), Key, 0, 10, Ct)).ShouldBeEmpty();
        var coverage = await store.CoverageAsync(Ct);
        coverage.Select(c => (c.Month, c.State, c.Count, c.Ungrounded)).Order().ShouldBe([("2026-07", "invalid_output", 1, 0), ("2026-07", "ok", 1, 0), ("2026-07", "ok", 1, 1)]);
        coverage.ShouldAllBe(c => c.PromptName == "announcement-flag" && c.PromptVersion == "v1" && c.SchemaVersion == "1");
    }

    [Fact]
    public async Task Without_a_session_the_coverage_route_is_401()
    {
        using var response = await Factory.CreateAnonymousClient().GetAsync(new Uri("/api/filings/extractions/coverage", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_coverage_route_answers_empty_with_nothing_stored_and_counts_with_no_text_when_there_is_something()
    {
        using var empty = await Client.GetAsync(new Uri("/api/filings/extractions/coverage", UriKind.Relative), Ct);
        var emptyBody = JsonDocument.Parse(await empty.Content.ReadAsStringAsync(Ct)).RootElement;
        var instrument = await SeedAsync("EXTB");
        using (var scope = Factory.Services.CreateScope())
        {
            var filing = FilingTestKit.Ok(instrument, "ext-cov", null, new DateTimeOffset(2026, 7, 24, 10, 0, 0, TimeSpan.Zero), FilingKind.Announcement);
            await scope.ServiceProvider.GetRequiredService<IAnnouncementStore>().InsertAsync(new NewAnnouncement(filing, "Order", "SECRET-HEADLINE"), Ct);
            await scope.ServiceProvider.GetRequiredService<IAnnouncementExtractionStore>().InsertAsync(new NewExtraction(filing.Id, Key, "ok", null, new AnnouncementAnswer(true, "order_win", "SECRET-REASON", null, "none", "none"), false, 25, 1, 1, FilingTestKit.Ingested), Ct);
        }

        using var response = await Client.GetAsync(new Uri("/api/filings/extractions/coverage", UriKind.Relative), Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        var body = JsonDocument.Parse(text).RootElement;

        empty.StatusCode.ShouldBe(HttpStatusCode.OK);
        (emptyBody.GetProperty("total").GetInt32(), emptyBody.GetProperty("rows").GetArrayLength()).ShouldBe((0, 0));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.GetProperty("total").GetInt32().ShouldBe(1);
        var row = body.GetProperty("rows").EnumerateArray().Single();
        row.EnumerateObject().Select(p => p.Name).Order().ShouldBe(["count", "modelSha256", "month", "promptName", "promptVersion", "schemaVersion", "state", "ungrounded"]);
        (row.GetProperty("month").GetString(), row.GetProperty("state").GetString(), row.GetProperty("promptVersion").GetString()).ShouldBe(("2026-07", "ok", "v1"));
        text.ShouldNotContain("SECRET");
    }
}
