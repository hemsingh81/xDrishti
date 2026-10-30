using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Infrastructure.Ai.Assistant;

namespace XDrishti.Api.IntegrationTests.Ai;

/// <summary>Spec P9-06 AC 10 on a throw-away server with the runtime role as in production: the briefing tables are insert-only, the first row wins, a new version is a new row, and the table rules hold.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class AssistantBriefingDatabaseTests(ResearchGrantsTests.DedicatedServer server) : IClassFixture<ResearchGrantsTests.DedicatedServer>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static NewBriefing Briefing(string subject, string source = "deterministic", string? reason = "model_off", string text = "text", bool newVersion = false, string kind = "daily") =>
        new(kind, subject, source, source == "model" ? null : reason, text, "det", """{"kind":"daily"}""", new string('a', 64), source == "model" ? "m.gguf" : null, null, "briefing.v1", """{"seed":1}""", 1, "test",
            [new BriefingAttemptRecord(1, "attempt text", source == "model", [new StoredFinding("number_not_in_facts", "98.60", 12)], 20, 100, 30)], newVersion);

    private async Task<bool> HasAsync(string table, string privilege)
    {
        await using var connection = new NpgsqlConnection(server.Owner);
        await connection.OpenAsync(Ct);
        await using var check = new NpgsqlCommand("SELECT has_table_privilege('xd_app', @t, @p)", connection);
        check.Parameters.AddWithValue("t", table);
        check.Parameters.AddWithValue("p", privilege);
        return (bool)(await check.ExecuteScalarAsync(Ct))!;
    }

    [Fact]
    public async Task The_runtime_role_can_insert_and_select_the_briefing_tables_but_not_update_delete_or_truncate_them()
    {
        foreach (var table in new[] { "ai.briefings", "ai.briefing_attempts" })
        {
            foreach (var (privilege, expected) in new[] { ("SELECT", true), ("INSERT", true), ("UPDATE", false), ("DELETE", false), ("TRUNCATE", false) })
            {
                (await HasAsync(table, privilege)).ShouldBe(expected, $"{privilege} on {table}");
            }
        }

        await using var source = NpgsqlDataSource.Create(server.App);
        await new BriefingStore(source).InsertAsync(Briefing("2026-10-20"), Ct);
        foreach (var sql in new[] { "UPDATE ai.briefings SET text = 'x'", "DELETE FROM ai.briefings", "UPDATE ai.briefing_attempts SET text = 'x'", "DELETE FROM ai.briefing_attempts", "TRUNCATE ai.briefings CASCADE" })
        {
            await using var command = source.CreateCommand(sql);
            (await Should.ThrowAsync<PostgresException>(async () => await command.ExecuteNonQueryAsync(Ct))).SqlState.ShouldBe("42501", sql);
        }
    }

    [Fact]
    public async Task The_first_row_of_a_subject_wins_a_repeat_writes_nothing_and_a_new_version_is_a_new_row()
    {
        await using var source = NpgsqlDataSource.Create(server.App);
        var store = new BriefingStore(source);

        var (first, created) = await store.InsertAsync(Briefing("2026-10-21", text: "first"), Ct);
        var (again, repeated) = await store.InsertAsync(Briefing("2026-10-21", source: "model", text: "second"), Ct);
        var (next, appended) = await store.InsertAsync(Briefing("2026-10-21", source: "model", text: "third", newVersion: true), Ct);

        (created, repeated, appended).ShouldBe((true, false, true));
        (first.Version, first.Text, first.Source, first.Reason).ShouldBe((1, "first", "deterministic", "model_off"));
        again.Id.ShouldBe(first.Id);
        again.Text.ShouldBe("first");
        (next.Version, next.Text, next.Source, next.Reason, next.ModelId).ShouldBe((2, "third", "model", null, "m.gguf"));
        (await store.FindAsync("daily", "2026-10-21", Ct))!.Version.ShouldBe(2);
        (await store.FindAsync("daily", "2026-10-22", Ct)).ShouldBeNull();
        var attempt = first.Attempts.ShouldHaveSingleItem();
        (attempt.Attempt, attempt.Passed, attempt.Text, attempt.LatencyMs, attempt.PromptTokens).ShouldBe((1, false, "attempt text", 20, 100));
        attempt.Findings.ShouldHaveSingleItem().ShouldBe(new StoredFinding("number_not_in_facts", "98.60", 12));
        (await store.ListAsync("daily", 60, Ct)).Count(r => r.Subject == "2026-10-21").ShouldBe(2);
        (await store.ListAsync("weekly", 60, Ct)).Count(r => r.Subject == "2026-10-21").ShouldBe(0);
    }

    [Fact]
    public async Task Rows_that_break_a_rule_of_the_tables_are_refused()
    {
        await using var source = NpgsqlDataSource.Create(server.App);
        var store = new BriefingStore(source);

        // A model text with no model id, a weekly text without the pinned line, a deterministic text with no reason, a bad kind.
        (await Should.ThrowAsync<PostgresException>(async () => await store.InsertAsync(Briefing("2026-10-23") with { ModelId = null, Source = "model", Reason = null }, Ct))).SqlState.ShouldBe("23514");
        (await Should.ThrowAsync<PostgresException>(async () => await store.InsertAsync(Briefing("2026-W43", kind: "weekly", text: "no footer"), Ct))).SqlState.ShouldBe("23514");
        (await Should.ThrowAsync<PostgresException>(async () => await store.InsertAsync(Briefing("2026-10-23") with { Reason = null }, Ct))).SqlState.ShouldBe("23514");
        (await Should.ThrowAsync<PostgresException>(async () => await store.InsertAsync(Briefing("2026-10-23", kind: "hourly"), Ct))).SqlState.ShouldBe("23514");
        (await store.InsertAsync(Briefing("2026-W43", kind: "weekly", text: "x. Nothing was applied: every item above is a proposal."), Ct)).Created.ShouldBeTrue();
        (await store.FindAsync("daily", "2026-10-23", Ct)).ShouldBeNull();
    }
}
