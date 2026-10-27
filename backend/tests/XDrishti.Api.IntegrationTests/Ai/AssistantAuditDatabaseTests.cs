using System.Diagnostics;
using System.Text.Json;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Domain.Assistant;
using XDrishti.Infrastructure.Ai.Assistant;

namespace XDrishti.Api.IntegrationTests.Ai;

/// <summary>
/// Spec P9-03 AC 7 and 10 on a throw-away server where the runtime role has the default privileges of the init script and the chain is migrated by the owner: every <c>ai</c> table is insert-only for <c>xd_app</c> (including <c>ai.settings</c> after this migration re-grants the schema), the helper view
/// stays closed, the tool server's roles reach none of the new tables, and the stores write and read a whole turn back.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class AssistantAuditDatabaseTests(ResearchGrantsTests.DedicatedServer server) : IClassFixture<ResearchGrantsTests.DedicatedServer>
{
    private const string Owner = "owner:owner@example.invalid";
    private static readonly string[] Tables = ["ai.briefing_attempts", "ai.briefings", "ai.conversations", "ai.messages", "ai.model_calls", "ai.prompts", "ai.settings", "ai.tool_calls"];
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<bool> HasAsync(string role, string table, string privilege)
    {
        await using var connection = new NpgsqlConnection(server.Owner);
        await connection.OpenAsync(Ct);
        await using var check = new NpgsqlCommand("SELECT has_table_privilege(@r, @t, @p)", connection);
        check.Parameters.AddWithValue("r", role);
        check.Parameters.AddWithValue("t", table);
        check.Parameters.AddWithValue("p", privilege);
        return (bool)(await check.ExecuteScalarAsync(Ct))!;
    }

    private async Task<(AssistantSettingsStore Settings, ConversationStore Conversations, NpgsqlDataSource Source)> StoresAsync()
    {
        var source = NpgsqlDataSource.Create(server.App);
        await Task.CompletedTask;
        return (new AssistantSettingsStore(source), new ConversationStore(source), source);
    }

    private async Task<long> CountAsync(string table, string where = "true")
    {
        await using var connection = new NpgsqlConnection(server.Owner);
        await connection.OpenAsync(Ct);
        await using var count = new NpgsqlCommand($"SELECT count(*) FROM {table} WHERE {where}", connection);
        return (long)(await count.ExecuteScalarAsync(Ct))!;
    }

    [Fact]
    public async Task Every_ai_table_is_insert_only_for_the_runtime_role_and_the_helper_view_stays_closed()
    {
        await using (var connection = new NpgsqlConnection(server.Owner))
        {
            await connection.OpenAsync(Ct);
            await using var tables = new NpgsqlCommand("SELECT tablename FROM pg_tables WHERE schemaname = 'ai' ORDER BY 1", connection);
            var names = new List<string>();
            await using var reader = await tables.ExecuteReaderAsync(Ct);
            while (await reader.ReadAsync(Ct))
            {
                names.Add("ai." + reader.GetString(0));
            }

            names.Order(StringComparer.Ordinal).ShouldBe(Tables.Order(StringComparer.Ordinal));
        }

        foreach (var table in Tables)
        {
            foreach (var (privilege, expected) in new[] { ("SELECT", true), ("INSERT", true), ("UPDATE", false), ("DELETE", false), ("TRUNCATE", false) })
            {
                (await HasAsync("xd_app", table, privilege)).ShouldBe(expected, $"{privilege} on {table}");
            }

            foreach (var role in new[] { "xd_mcp_read", "xd_mcp_sql" })
            {
                foreach (var privilege in new[] { "INSERT", "UPDATE", "DELETE", "TRUNCATE" })
                {
                    (await HasAsync(role, table, privilege)).ShouldBeFalse($"{role} {privilege} on {table}");
                }

                if (table != "ai.settings")
                {
                    (await HasAsync(role, table, "SELECT")).ShouldBeFalse($"{role} SELECT on {table}");
                }
            }
        }

        (await HasAsync("xd_mcp_read", "ai.settings", "SELECT")).ShouldBeTrue("the read role still reads the switch");
        (await HasAsync("xd_mcp_sql", "ai.settings", "SELECT")).ShouldBeFalse();
        foreach (var privilege in new[] { "SELECT", "INSERT", "UPDATE", "DELETE" })
        {
            (await HasAsync("xd_app", "ai.account_labels", privilege)).ShouldBeFalse($"{privilege} on ai.account_labels");
        }
    }

    [Fact]
    public async Task As_the_runtime_role_an_update_or_delete_of_any_ai_row_is_refused_by_the_database()
    {
        var (settings, conversations, source) = await StoresAsync();
        await using var _ = source;
        (await settings.AppendAsync(AssistantSettingsChange.Create(Owner, "grants test", false, false, 6).Value, await CurrentIdAsync(settings), Ct))!.Id.ShouldNotBeNull();
        var started = await conversations.StartTurnAsync(Start(null), Ct);
        await conversations.CompleteTurnAsync(Answer(started.ConversationId), Ct);

        foreach (var sql in new[]
        {
            "UPDATE ai.settings SET reason = 'x'", "DELETE FROM ai.settings",
            "UPDATE ai.conversations SET title = 'x'", "DELETE FROM ai.conversations",
            "UPDATE ai.messages SET content = 'x'", "DELETE FROM ai.messages",
            "UPDATE ai.prompts SET text = 'x'", "DELETE FROM ai.prompts",
            "UPDATE ai.tool_calls SET status = 'ok'", "DELETE FROM ai.tool_calls",
            "UPDATE ai.model_calls SET caller = 'x'", "DELETE FROM ai.model_calls",
            "UPDATE ai.briefings SET text = 'x'", "DELETE FROM ai.briefings",
            "UPDATE ai.briefing_attempts SET text = 'x'", "DELETE FROM ai.briefing_attempts",
            "TRUNCATE ai.messages CASCADE",
        })
        {
            await using var command = source.CreateCommand(sql);
            var failure = await Should.ThrowAsync<PostgresException>(async () => await command.ExecuteNonQueryAsync(Ct));
            failure.SqlState.ShouldBe("42501", sql);
        }
    }

    private static async Task<long?> CurrentIdAsync(AssistantSettingsStore store) => (await store.CurrentAsync(Ct)).Id;

    private static StartTurnRecord Start(Guid? conversation, string question = "How is ORB15 doing?") =>
        new(conversation, Guid.NewGuid(), Owner, "chat", question, new string('a', 64), "chat", "v1", "the prompt text", null, "xd-main", "b1");

    private static AssistantMessageRecord Answer(Guid conversation, Guid? turn = null) =>
        new(conversation, turn ?? Guid.NewGuid(), "Expectancy is 0.12R.", new string('a', 64), "xd-main", 10, 5, 100, 900, "stop", "grounded", "[]");

    [Fact]
    public async Task The_settings_store_appends_rows_and_the_newest_is_current_and_a_stale_expectation_writes_nothing()
    {
        var (settings, _, source) = await StoresAsync();
        await using var _ = source;
        var start = await settings.CurrentAsync(Ct);
        var rowsBefore = await CountAsync("ai.settings");

        var on = await settings.AppendAsync(AssistantSettingsChange.Create(Owner, "on", true, true, 8).Value, start.Id, Ct);
        var stale = await settings.AppendAsync(AssistantSettingsChange.Create(Owner, "x", false, false, 2).Value, start.Id, Ct);
        var off = await settings.AppendAsync(AssistantSettingsChange.Create(Owner, "off", false, false, 8).Value, on!.Id, Ct);

        on.ShouldNotBeNull();
        (on.Enabled, on.PortfolioContext, on.MaxToolCalls).ShouldBe((true, true, 8));
        stale.ShouldBeNull();
        off.ShouldNotBeNull();
        (await settings.CurrentAsync(Ct)).ShouldBe(off);
        (await CountAsync("ai.settings")).ShouldBe(rowsBefore + 2);
        (await CountAsync("ai.settings", "changed_by NOT LIKE 'owner:%'")).ShouldBe(0);
        (await CountAsync("ai.settings", "length(snapshot_hash) = 64")).ShouldBe(rowsBefore + 2);
    }

    [Fact]
    public async Task A_whole_turn_is_stored_and_read_back_through_the_three_reads()
    {
        var (_, store, source) = await StoresAsync();
        await using var _ = source;
        var turn = Guid.NewGuid();
        var first = await store.StartTurnAsync(Start(null, "How is ORB15 doing?\n\n<b>bold</b> مرحبا 😀") with { TurnId = turn }, Ct);
        var envelope = $$"""{"ok":true,"tool":"get_report","rows":[{"n":143}],"rowCount":1,"truncated":false,"pad":"{{new string('x', 3000)}}"}""";
        var callId = await store.RecordToolCallAsync(new ToolCallRecord(first.ConversationId, turn, 1, "get_report", """{"report":"summary"}""", "ok", null, envelope, envelope.Length, 1, false, """{"basis":"gross","n":143,"dateRange":"2025"}""", 1, 2, DateTimeOffset.UtcNow, 12), Ct);
        var messageId = await store.CompleteTurnAsync(Answer(first.ConversationId, turn) with { UngroundedJson = """[{"start":3,"end":5,"text":"46"}]""", GroundingStatus = "ungrounded" }, Ct);

        var list = await store.ListAsync(20, Ct);
        var detail = await store.GetAsync(first.ConversationId, Ct);
        var call = await store.GetToolCallAsync(callId, Ct);

        list.Single(c => c.Id == first.ConversationId).MessageCount.ShouldBe(2);
        detail!.Title.ShouldContain("<b>bold</b>");
        detail.Title.ShouldNotContain("\n");
        detail.Messages.Select(m => (m.Seq, m.Role)).ShouldBe([(1, "user"), (2, "assistant")]);
        detail.Messages[0].Content.ShouldContain("😀");
        detail.Messages[0].NoAnswerStored.ShouldBeFalse();
        var assistant = detail.Messages[1];
        (assistant.Id, assistant.FinishReason, assistant.GroundingStatus, assistant.PromptTokens, assistant.FirstTokenMs).ShouldBe((messageId, "stop", "ungrounded", 10, 100));
        assistant.Ungrounded.ShouldHaveSingleItem().Text.ShouldBe("46");
        var summary = assistant.ToolCalls.ShouldHaveSingleItem();
        (summary.Index, summary.Tool, summary.Status, summary.Rows, summary.DurationMs).ShouldBe((1, "get_report", "ok", 1, 12));
        summary.Arguments.GetProperty("report").GetString().ShouldBe("summary");
        summary.Evidence!.Value.GetProperty("basis").GetString().ShouldBe("gross");
        call!.Result!.Value.GetProperty("rowCount").GetInt32().ShouldBe(1);
        (call.Redactions, call.InjectionFlags).ShouldBe((1, 2));
        (await store.GetToolCallAsync(long.MaxValue, Ct)).ShouldBeNull();
        (await store.GetAsync(Guid.NewGuid(), Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task A_prompt_is_stored_once_per_hash_and_a_conversation_continues_with_the_next_seq_and_a_history_window()
    {
        var (_, store, source) = await StoresAsync();
        await using var _ = source;
        var first = await store.StartTurnAsync(Start(null), Ct);
        await store.CompleteTurnAsync(Answer(first.ConversationId), Ct);
        var second = await store.StartTurnAsync(Start(first.ConversationId, "and the second?"), Ct);
        await store.CompleteTurnAsync(Answer(second.ConversationId) with { Content = "second answer" }, Ct);

        second.ConversationId.ShouldBe(first.ConversationId);
        second.UserSeq.ShouldBe(3);
        (await CountAsync("ai.prompts", $"hash = '{new string('a', 64)}'")).ShouldBe(1);
        (await store.ExistsAsync(first.ConversationId, Ct)).ShouldBeTrue();
        (await store.ExistsAsync(Guid.NewGuid(), Ct)).ShouldBeFalse();
        var recent = await store.RecentAsync(first.ConversationId, 3, Ct);
        recent.Select(m => m.Content).ShouldBe(["Expectancy is 0.12R.", "and the second?", "second answer"]);
    }

    [Fact]
    public async Task A_turn_without_an_assistant_message_is_shown_as_no_answer_stored_and_a_cancelled_one_keeps_its_partial_text()
    {
        var (_, store, source) = await StoresAsync();
        await using var _ = source;
        var crashed = await store.StartTurnAsync(Start(null, "crash"), Ct);
        var cancelledTurn = Guid.NewGuid();
        var cancelled = await store.StartTurnAsync(Start(null, "stop") with { TurnId = cancelledTurn }, Ct);
        await store.CompleteTurnAsync(Answer(cancelled.ConversationId, cancelledTurn) with { Content = "partial te", FinishReason = "cancelled", GroundingStatus = "not_checked" }, Ct);

        (await store.GetAsync(crashed.ConversationId, Ct))!.Messages.Single().NoAnswerStored.ShouldBeTrue();
        var detail = (await store.GetAsync(cancelled.ConversationId, Ct))!;
        (detail.Messages[1].Content, detail.Messages[1].FinishReason, detail.Messages[1].GroundingStatus).ShouldBe(("partial te", "cancelled", "not_checked"));
    }

    [Fact]
    public async Task Text_with_a_nul_character_is_stored_with_a_replacement_and_a_non_json_result_is_stored_as_a_string()
    {
        var (_, store, source) = await StoresAsync();
        await using var _ = source;
        var turn = Guid.NewGuid();
        var started = await store.StartTurnAsync(Start(null, "nul") with { TurnId = turn }, Ct);
        var id = await store.RecordToolCallAsync(new ToolCallRecord(started.ConversationId, turn, 1, "run_sql", "not json", "error", "tool.invalid_result", "plain text \0 not json", 20, 0, false, null, 0, 0, DateTimeOffset.UtcNow, 3), Ct);
        await store.CompleteTurnAsync(Answer(started.ConversationId, turn) with { Content = "a\0b" }, Ct);

        var call = await store.GetToolCallAsync(id, Ct);

        call!.Arguments.ValueKind.ShouldBe(JsonValueKind.String);
        call.Result!.Value.GetString().ShouldBe("plain text � not json");
        (await store.GetAsync(started.ConversationId, Ct))!.Messages[1].Content.ShouldBe("a�b");
    }

    [Fact]
    public async Task The_same_call_number_twice_in_a_turn_and_the_same_message_number_twice_are_refused_by_the_database()
    {
        var (_, store, source) = await StoresAsync();
        await using var _ = source;
        var turn = Guid.NewGuid();
        var started = await store.StartTurnAsync(Start(null, "dup") with { TurnId = turn }, Ct);
        var call = new ToolCallRecord(started.ConversationId, turn, 1, "get_report", "{}", "ok", null, "{}", 2, 0, false, null, 0, 0, DateTimeOffset.UtcNow, 1);
        await store.RecordToolCallAsync(call, Ct);

        var failure = await Should.ThrowAsync<PostgresException>(async () => await store.RecordToolCallAsync(call, Ct));
        failure.SqlState.ShouldBe("23505");
    }

    [Fact]
    public async Task The_conversation_list_is_newest_first_empty_when_there_is_none_and_fast_for_a_thousand()
    {
        await using var connection = new NpgsqlConnection(server.Owner);
        await connection.OpenAsync(Ct);
        await using (var clear = new NpgsqlCommand("DELETE FROM ai.tool_calls; DELETE FROM ai.messages; DELETE FROM ai.conversations", connection))
        {
            await clear.ExecuteNonQueryAsync(Ct);
        }

        var (_, store, source) = await StoresAsync();
        await using var _ = source;
        (await store.ListAsync(20, Ct)).ShouldBeEmpty();

        await using (var seed = new NpgsqlCommand("""
            INSERT INTO ai.conversations (id, created_at, created_by, title, origin, model_id)
            SELECT gen_random_uuid(), now() - (g || ' minutes')::interval, 'owner:o@example.invalid', 'c' || g, 'chat', 'xd-main' FROM generate_series(1, 1000) g;
            INSERT INTO ai.messages (conversation_id, turn_id, seq, role, content)
            SELECT c.id, gen_random_uuid(), 1, 'user', 'q' FROM ai.conversations c;
            ANALYZE ai.conversations; ANALYZE ai.messages;
            """, connection))
        {
            await seed.ExecuteNonQueryAsync(Ct);
        }

        var samples = new List<long>();
        for (var i = 0; i < 20; i++)
        {
            var watch = Stopwatch.StartNew();
            var rows = await store.ListAsync(20, Ct);
            samples.Add(watch.ElapsedMilliseconds);
            rows.Count.ShouldBe(20);
            rows.Select(r => r.Title).ShouldBe(Enumerable.Range(1, 20).Select(g => "c" + g));
        }

        samples.Order().ElementAt(18).ShouldBeLessThan(100, "p95 of the list over 1,000 conversations");
    }

    [Fact]
    public async Task The_nightly_probe_sees_a_running_run_started_inside_the_window_and_nothing_else()
    {
        var (_, _, source) = await StoresAsync();
        await using var _ = source;
        var probe = new NightlyRunProbe(source, TimeProvider.System);
        await ExecuteAsync("DELETE FROM platform.service_runs WHERE service_id = 'nightly'");
        (await probe.IsRunningAsync(TimeSpan.FromMinutes(60), Ct)).ShouldBeFalse();

        await ExecuteAsync("INSERT INTO platform.service_runs (id, service_id, trigger, attempt, started_at, status) VALUES (gen_random_uuid(), 'nightly', 'scheduled', 1, now() - interval '90 minutes', 'running')");
        (await probe.IsRunningAsync(TimeSpan.FromMinutes(60), Ct)).ShouldBeFalse("started over 60 minutes ago");
        await ExecuteAsync("UPDATE platform.service_runs SET status = 'failed', finished_at = now() WHERE service_id = 'nightly'");
        await ExecuteAsync("INSERT INTO platform.service_runs (id, service_id, trigger, attempt, started_at, status) VALUES (gen_random_uuid(), 'nightly', 'scheduled', 1, now() - interval '10 minutes', 'running')");
        (await probe.IsRunningAsync(TimeSpan.FromMinutes(60), Ct)).ShouldBeTrue();
        await ExecuteAsync("INSERT INTO platform.service_runs (id, service_id, trigger, attempt, started_at, status) VALUES (gen_random_uuid(), 'eod', 'scheduled', 1, now(), 'running')");
        await ExecuteAsync("UPDATE platform.service_runs SET status = 'succeeded', finished_at = now() WHERE service_id = 'nightly'");
        (await probe.IsRunningAsync(TimeSpan.FromMinutes(60), Ct)).ShouldBeFalse("another service running does not count");
    }

    [Fact]
    public async Task A_model_call_outside_chat_is_recorded_without_content()
    {
        var (_, _, source) = await StoresAsync();
        await using var _ = source;
        var audit = new ModelCallAudit(source);

        await audit.RecordAsync(new ModelCallRecord("briefing", "service:briefing", new string('b', 64), "xd-main", new string('c', 64), 1000, 200, 5000, "stop", "ok", null), Ct);
        await audit.RecordAsync(new ModelCallRecord("eval", "service:eval", new string('b', 64), "xd-main", null, null, null, 180000, "error", "timeout", "assistant.timeout"), Ct);

        (await CountAsync("ai.model_calls", "purpose = 'briefing' AND status = 'ok' AND prompt_tokens = 1000")).ShouldBe(1);
        (await CountAsync("ai.model_calls", "purpose = 'eval' AND error_code = 'assistant.timeout' AND model_sha256 IS NULL")).ShouldBe(1);
    }

    [Fact]
    public async Task Rows_that_break_a_rule_of_the_tables_are_refused_a_non_owner_settings_actor_a_bad_origin_a_bad_status()
    {
        var (_, _, source) = await StoresAsync();
        await using var _ = source;
        foreach (var sql in new[]
        {
            "INSERT INTO ai.settings (enabled, portfolio_context, max_tool_calls, changed_by, reason) VALUES (true, false, 6, 'service:eval', 'x')",
            "INSERT INTO ai.settings (enabled, portfolio_context, max_tool_calls, changed_by, reason) VALUES (true, false, 13, 'owner:o@example.invalid', 'x')",
            "INSERT INTO ai.conversations (id, created_by, title, origin, model_id) VALUES (gen_random_uuid(), 'o', 't', 'robot', 'm')",
            "INSERT INTO ai.model_calls (purpose, caller, prompt_hash, model_id, total_ms, finish_reason, status) VALUES ('chat', 'c', 'h', 'm', 1, 'stop', 'ok')",
        })
        {
            await using var command = source.CreateCommand(sql);
            var failure = await Should.ThrowAsync<PostgresException>(async () => await command.ExecuteNonQueryAsync(Ct));
            failure.SqlState.ShouldBeOneOf(["23514", "22001"], sql);
        }
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(server.Owner);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }
}
