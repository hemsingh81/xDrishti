using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Infrastructure.Ai.Assistant;

namespace XDrishti.Api.IntegrationTests.Ai;

/// <summary>Spec P9-07 AC 11 and the migration test on a throw-away server with the runtime role as in production: the draft tables are insert-only, a draft has one decision, the rules of the tables hold, and each of the four new switches changes the settings hash.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class AssistantDraftDatabaseTests(ResearchGrantsTests.DedicatedServer server) : IClassFixture<ResearchGrantsTests.DedicatedServer>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static NewDraft Draft(long id, string status = "valid", string? yaml = "id: X\n") =>
        new(id, "an idea of the owner", status, status == "failed" ? "timeout" : null, "job", null, yaml, null, [], [], [], "m.gguf", null, "draft.v1", new string('d', 64), """{"seed":1}""", "owner:o@example.invalid");

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
    public async Task The_runtime_role_can_insert_and_select_the_draft_tables_but_not_update_delete_or_truncate_them()
    {
        foreach (var table in new[] { "ai.strategy_drafts", "ai.strategy_draft_decisions" })
        {
            foreach (var (privilege, expected) in new[] { ("SELECT", true), ("INSERT", true), ("UPDATE", false), ("DELETE", false), ("TRUNCATE", false) })
            {
                (await HasAsync(table, privilege)).ShouldBe(expected, $"{privilege} on {table}");
            }
        }

        await using var source = NpgsqlDataSource.Create(server.App);
        var store = new DraftStore(source);
        await store.InsertAsync(Draft(await store.ReserveIdAsync(Ct)), Ct);
        foreach (var sql in new[] { "UPDATE ai.strategy_drafts SET idea = 'x'", "DELETE FROM ai.strategy_drafts", "UPDATE ai.strategy_draft_decisions SET actor = 'x'", "DELETE FROM ai.strategy_draft_decisions", "TRUNCATE ai.strategy_drafts CASCADE" })
        {
            await using var command = source.CreateCommand(sql);
            (await Should.ThrowAsync<PostgresException>(async () => await command.ExecuteNonQueryAsync(Ct))).SqlState.ShouldBe("42501", sql);
        }
    }

    [Fact]
    public async Task A_draft_is_stored_once_with_its_reserved_id_read_back_and_has_one_decision()
    {
        await using var source = NpgsqlDataSource.Create(server.App);
        var store = new DraftStore(source);
        var id = await store.ReserveIdAsync(Ct);
        await store.InsertAsync(Draft(id), Ct);

        var read = (await store.GetAsync(id, Ct))!;
        var first = await store.InsertDecisionAsync(new NewDraftDecision(id, "confirmed", "X", 1, null, true, false, null, null, "owner:o@example.invalid"), Ct);
        var second = await store.InsertDecisionAsync(new NewDraftDecision(id, "discarded", null, null, null, false, false, null, null, "owner:o@example.invalid"), Ct);

        (read.Idea, read.Status, read.Yaml, read.Via, read.PromptVersion).ShouldBe(("an idea of the owner", "valid", "id: X\n", "job", "draft.v1"));
        (first.Created, second.Created).ShouldBe((true, false));
        second.Decision.Decision.ShouldBe("confirmed");
        (await store.DecisionAsync(id, Ct))!.Edited.ShouldBeTrue();
        await Should.ThrowAsync<PostgresException>(async () => await store.InsertAsync(Draft(id), Ct));
        (await store.ListAsync(5, Ct))[0].Id.ShouldBe(id);
        (await store.CountBetweenAsync(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1), Ct)).ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Rows_that_break_a_rule_of_the_tables_are_refused()
    {
        await using var source = NpgsqlDataSource.Create(server.App);
        var store = new DraftStore(source);

        // a valid draft with no text, a bad status, a confirmed decision with no strategy, an auto decision with no setting snapshot or the wrong actor, a decision for no draft.
        (await Should.ThrowAsync<PostgresException>(async () => await store.InsertAsync(Draft(await store.ReserveIdAsync(Ct), yaml: null), Ct))).SqlState.ShouldBe("23514");
        (await Should.ThrowAsync<PostgresException>(async () => await store.InsertAsync(Draft(await store.ReserveIdAsync(Ct), status: "confirmed"), Ct))).SqlState.ShouldBe("23514");
        var id = await store.ReserveIdAsync(Ct);
        await store.InsertAsync(Draft(id, "declined", null), Ct);
        (await Should.ThrowAsync<PostgresException>(async () => await store.InsertDecisionAsync(new NewDraftDecision(id, "confirmed", null, null, null, false, false, null, null, "owner:o@example.invalid"), Ct))).SqlState.ShouldBe("23514");
        (await Should.ThrowAsync<PostgresException>(async () => await store.InsertDecisionAsync(new NewDraftDecision(id, "confirmed", "X", 1, null, false, true, null, null, "system:auto-draft"), Ct))).SqlState.ShouldBe("23514");
        (await Should.ThrowAsync<PostgresException>(async () => await store.InsertDecisionAsync(new NewDraftDecision(id + 1000, "discarded", null, null, null, false, false, null, null, "owner:o@example.invalid"), Ct))).SqlState.ShouldBe("23503");
        (await store.InsertDecisionAsync(new NewDraftDecision(id, "confirmed", "X", 1, null, false, true, 1, new string('a', 64), "system:auto-draft"), Ct)).Created.ShouldBeTrue();
    }

    [Fact]
    public async Task Each_new_switch_changes_the_settings_hash_a_row_with_none_keeps_the_old_text_and_the_switches_default_to_off()
    {
        await using var connection = new NpgsqlConnection(server.Owner);
        await connection.OpenAsync(Ct);

        async Task<string> HashAsync(string columns, string values)
        {
            await using var insert = new NpgsqlCommand($"INSERT INTO ai.settings (enabled, portfolio_context, max_tool_calls, changed_by, reason{columns}) VALUES (true, false, 6, 'owner:o@example.invalid', 'test'{values}) RETURNING snapshot_hash", connection);
            return (string)(await insert.ExecuteScalarAsync(Ct))!;
        }

        var plain = await HashAsync(string.Empty, string.Empty);
        var explicitOff = await HashAsync(", allow_draft_confirm, auto_lab_quick_test, allow_proposal_accept, auto_accept_hypothesis", ", false, false, false, false");
        var hashes = new List<string> { plain };
        foreach (var column in new[] { "allow_draft_confirm", "auto_lab_quick_test", "allow_proposal_accept", "auto_accept_hypothesis" })
        {
            hashes.Add(await HashAsync(", " + column, ", true"));
        }

        explicitOff.ShouldBe(plain);
        hashes.Distinct().Count().ShouldBe(5);
        var expectedOld = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("ai-settings:v1|enabled=true|portfolio_context=false|max_tool_calls=6")));
        plain.ShouldBe(expectedOld);
        await using var settings = NpgsqlDataSource.Create(server.App);
        var current = await new AssistantSettingsStore(settings).CurrentAsync(Ct);
        (current.AllowDraftConfirm, current.AutoLabQuickTest, current.AllowProposalAccept, current.AutoAcceptHypothesis).ShouldBe((false, false, false, true));
    }
}
