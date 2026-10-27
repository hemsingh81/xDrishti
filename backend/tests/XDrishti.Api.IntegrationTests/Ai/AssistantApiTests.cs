using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Ai;

namespace XDrishti.Api.IntegrationTests.Ai;

/// <summary>
/// Spec P9-03 end to end on the real API with a scripted model and tool server: off by default, the refusals in words (nothing stored), the stream and its frames, the owner's settings change (password only to turn on or raise, never from the body's actor), cancellation, the single turn slot,
/// the rate limit, the reads and the audit. The database is the shared test server; the stream is read from the response while it is produced.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class AssistantApiTests(ApiFactory factory)
{
    private const string Owner = "owner:" + ApiFactory.OwnerEmail;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Rig(WebApplicationFactory<Program> Host, ScriptedModel Model, ScriptedTools Tools, ScriptedHealth Health, TaskCompletionSource Stored);

    private Rig NewRig(Action<IWebHostBuilder>? configure = null)
    {
        var model = new ScriptedModel();
        var tools = new ScriptedTools();
        var health = new ScriptedHealth();
        var stored = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = factory.WithWebHostBuilder(b =>
        {
            b.ConfigureTestServices(services =>
            {
                services.RemoveAll<IChatModel>().AddSingleton<IChatModel>(model);
                services.RemoveAll<IToolCatalog>().AddSingleton<IToolCatalog>(tools);
                services.RemoveAll<ILlmHealth>().AddSingleton<ILlmHealth>(health);
                services.RemoveAll<IConversationStore>().AddScoped<IConversationStore>(sp => new SignallingStore(sp.GetRequiredService<XDrishti.Infrastructure.Ai.Assistant.ConversationStore>(), stored));
            });
            configure?.Invoke(b);
        });
        return new Rig(host, model, tools, health, stored);
    }

    private async Task<HttpClient> SignedInAsync(WebApplicationFactory<Program> host)
    {
        var anonymous = factory.CreateAnonymousClient();
        using var login = await anonymous.LoginAsync(ApiFactory.OwnerEmail, factory.OwnerPassword, Ct);
        var session = (await login.Content.ReadFromJsonAsync<XDrishti.Application.Auth.SessionInfo>(Ct))!;
        var cookie = ApiFactory.SessionCookieValue(login)!;
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", $"198.19.{Random.Shared.Next(1, 250)}.{Random.Shared.Next(1, 250)}");
        client.DefaultRequestHeaders.Add("Cookie", $"xd.sid={cookie}");
        client.DefaultRequestHeaders.Add("X-CSRF-Token", session.CsrfToken);
        client.Timeout = TimeSpan.FromSeconds(60);
        return client;
    }

    private Task<int> ExecuteAsync(string sql) => factory.WithDbAsync(async db => await db.Database.ExecuteSqlRawAsync(sql, Ct));

    private async Task ResetAsync()
    {
        await ExecuteAsync("DELETE FROM ai.tool_calls; DELETE FROM ai.messages; DELETE FROM ai.conversations; DELETE FROM ai.model_calls; DELETE FROM ai.settings; DELETE FROM platform.service_runs WHERE service_id = 'nightly'");
    }

    private Task<int> SwitchOnAsync(int budget = 6) =>
        ExecuteAsync($"INSERT INTO ai.settings (enabled, portfolio_context, max_tool_calls, changed_by, reason) VALUES (true, false, {budget}, '{Owner}', 'test setup')");

#pragma warning disable EF1002 // the table names and conditions are the constants of these tests
    private async Task<long> CountAsync(string table, string where = "true") =>
        await factory.WithDbAsync(async db => await db.Database.SqlQueryRaw<long>($"SELECT count(*) AS \"Value\" FROM {table} WHERE {where}").SingleAsync(Ct));

    private async Task<long> AuditRowsAsync() =>
        await CountAsync("ai.conversations") + await CountAsync("ai.messages") + await CountAsync("ai.tool_calls") + await CountAsync("ai.model_calls");

    private static StringContent Chat(string? message, Guid? conversation = null) =>
        new(JsonSerializer.Serialize(new { conversationId = conversation, message }), Encoding.UTF8, "application/json");

    private static Task<HttpResponseMessage> PostChatAsync(HttpClient client, string? message, Guid? conversation = null) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Post, new Uri("/api/assistant/chat", UriKind.Relative)) { Content = Chat(message, conversation) }, HttpCompletionOption.ResponseHeadersRead, Ct);

    private sealed record Frame(string Event, JsonElement Data);

    private static async IAsyncEnumerable<Frame> FramesAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(Ct);
        using var reader = new StreamReader(stream);
        string? name = null;
        var data = new StringBuilder();
        while (await reader.ReadLineAsync(Ct) is { } line)
        {
            if (line.Length == 0)
            {
                if (name is not null)
                {
                    using var document = JsonDocument.Parse(data.ToString());
                    yield return new Frame(name, document.RootElement.Clone());
                }

                name = null;
                data.Clear();
            }
            else if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                name = line[6..].Trim();
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                data.Append(line[5..].TrimStart());
            }
        }
    }

    private static async Task<List<Frame>> AllFramesAsync(HttpResponseMessage response)
    {
        var frames = new List<Frame>();
        await foreach (var frame in FramesAsync(response))
        {
            frames.Add(frame);
        }

        return frames;
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response) => (await response.ReadProblemAsync(Ct)).Code;

    [Fact]
    public async Task Every_route_needs_a_session_and_the_writes_need_the_csrf_token()
    {
        await ResetAsync();
        using var host = NewRig().Host;
        var anonymous = factory.CreateAnonymousClient();
        var routes = new (HttpMethod Method, string Path)[]
        {
            (HttpMethod.Get, "/api/assistant/status"), (HttpMethod.Get, "/api/assistant/conversations"), (HttpMethod.Get, $"/api/assistant/conversations/{Guid.NewGuid()}"), (HttpMethod.Get, "/api/assistant/tool-calls/1"),
            (HttpMethod.Post, "/api/assistant/chat"), (HttpMethod.Put, "/api/assistant/settings"),
        };
        foreach (var (method, path) in routes)
        {
            using var response = await anonymous.SendAsync(new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = method == HttpMethod.Get ? null : new StringContent("{}", Encoding.UTF8, "application/json") }, Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, path);
        }

        var signedIn = await factory.SignInAsync(Ct);
        var noToken = factory.CreateAnonymousClient();
        noToken.DefaultRequestHeaders.Add("Cookie", signedIn.CookieHeader);
        using var chat = await noToken.PostAsync(new Uri("/api/assistant/chat", UriKind.Relative), Chat("hello"), Ct);
        using var settings = await noToken.PutAsync(new Uri("/api/assistant/settings", UriKind.Relative), new StringContent("""{"enabled":false,"portfolioContext":false,"maxToolCalls":6,"reason":"x"}""", Encoding.UTF8, "application/json"), Ct);
        chat.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        settings.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await AuditRowsAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task By_default_the_status_says_off_with_the_budget_of_six_and_no_row()
    {
        await ResetAsync();
        var rig = NewRig();
        using var host = rig.Host;
        var client = await SignedInAsync(host);

        using var response = await client.GetAsync("/api/assistant/status", Ct);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        (json.GetProperty("enabled").GetBoolean(), json.GetProperty("portfolioContext").GetBoolean(), json.GetProperty("maxToolCalls").GetInt32(), json.GetProperty("settingsId").ValueKind).ShouldBe((false, false, 6, JsonValueKind.Null));
        json.GetProperty("tools").GetProperty("state").GetString().ShouldBe("disabled");
        json.GetProperty("llm").GetProperty("state").GetString().ShouldBe("ready");
        json.GetProperty("evaluation").GetProperty("state").GetString().ShouldBe("not_yet_evaluated");
        (json.GetProperty("busy").GetBoolean(), json.GetProperty("nightlyRunning").GetBoolean()).ShouldBe((false, false));
        rig.Tools.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Asked_while_off_the_chat_is_a_409_and_nothing_is_called_or_stored()
    {
        await ResetAsync();
        var rig = NewRig();
        using var host = rig.Host;
        var client = await SignedInAsync(host);

        using var response = await PostChatAsync(client, "How is ORB15 doing?");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeAsync(response)).ShouldBe("assistant.disabled");
        (rig.Model.Calls, rig.Tools.Calls, rig.Health.Calls).ShouldBe((0, 0, 0));
        (await AuditRowsAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task A_tool_turn_streams_in_order_with_the_right_headers_and_is_stored_and_read_back()
    {
        await ResetAsync();
        await SwitchOnAsync();
        var rig = NewRig();
        using var host = rig.Host;
        rig.Model.Script(new ModelTextPart("Let me look."), new ModelToolCallPart(new ModelToolCall("c1", "get_report", """{"report":"summary"}""")), new ModelFinishPart("tool_calls", 50, 5));
        rig.Model.Script(new ModelTextPart("Expectancy is 0.12R "), new ModelTextPart("over 143 trades [1], about 46 won."), new ModelFinishPart("stop", 80, 12));
        var client = await SignedInAsync(host);

        using var response = await PostChatAsync(client, "How is ORB15 doing?");
        var frames = await AllFramesAsync(response);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/event-stream");
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        response.Content.Headers.ContentEncoding.ShouldBeSubsetOf(["identity"]); // never gzip, br or zstd: a compressor would hold the stream back
        frames.Select(f => f.Event).ShouldBe(["turn", "status", "token", "iteration_discard", "tool_call", "tool_result", "status", "token", "token", "done"]);
        var turn = frames[0].Data;
        turn.GetProperty("promptHash").GetString()!.Length.ShouldBe(64);
        turn.GetProperty("model").GetProperty("id").GetString().ShouldBe("xd-main");
        turn.GetProperty("model").GetProperty("sha256").GetString().ShouldBe("0d003f6662faee786ed5da3e31b29c978de5ae5d275c8794c606a7f3c01aa8f5");
        turn.GetProperty("settingsId").GetInt64().ShouldBeGreaterThan(0);
        frames[1].Data.GetProperty("phase").GetString().ShouldBe("reading");
        frames[4].Data.GetProperty("tool").GetString().ShouldBe("get_report");
        frames[4].Data.GetProperty("arguments").GetProperty("report").GetString().ShouldBe("summary");
        var toolResult = frames[5].Data;
        (toolResult.GetProperty("status").GetString(), toolResult.GetProperty("rowCount").GetInt32(), toolResult.GetProperty("evidence").GetProperty("basis").GetString()).ShouldBe(("ok", 1, "gross"));
        var done = frames[^1].Data;
        (done.GetProperty("finishReason").GetString(), done.GetProperty("grounding").GetProperty("status").GetString()).ShouldBe(("stop", "ungrounded"));
        done.GetProperty("grounding").GetProperty("ungrounded")[0].GetProperty("text").GetString().ShouldBe("46");
        done.GetProperty("usage").GetProperty("promptTokens").GetInt32().ShouldBe(130);

        var conversationId = turn.GetProperty("conversationId").GetGuid();
        (await CountAsync("ai.conversations")).ShouldBe(1);
        (await CountAsync("ai.messages")).ShouldBe(2);
        (await CountAsync("ai.tool_calls", "injection_flags = 1")).ShouldBe(1);
        (await CountAsync("ai.prompts", $"hash = '{turn.GetProperty("promptHash").GetString()}'")).ShouldBe(1);
        (await CountAsync("ai.conversations", $"origin = 'chat' AND created_by = '{Owner}'")).ShouldBe(1);

        var list = await client.GetFromJsonAsync<JsonElement>("/api/assistant/conversations", Ct);
        list.GetArrayLength().ShouldBe(1);
        list[0].GetProperty("messageCount").GetInt32().ShouldBe(2);
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/assistant/conversations/{conversationId}", Ct);
        var answer = detail.GetProperty("messages")[1];
        answer.GetProperty("role").GetString().ShouldBe("assistant");
        answer.GetProperty("content").GetString().ShouldBe("Expectancy is 0.12R over 143 trades [1], about 46 won.");
        answer.GetProperty("groundingStatus").GetString().ShouldBe("ungrounded");
        var call = answer.GetProperty("toolCalls")[0];
        (call.GetProperty("tool").GetString(), call.GetProperty("status").GetString(), call.GetProperty("rows").GetInt32()).ShouldBe(("get_report", "ok", 1));
        var stored = await client.GetFromJsonAsync<JsonElement>($"/api/assistant/tool-calls/{call.GetProperty("id").GetInt64()}", Ct);
        stored.GetProperty("result").GetProperty("rowCount").GetInt32().ShouldBe(1);
        stored.GetProperty("arguments").GetProperty("report").GetString().ShouldBe("summary");
        rig.Tools.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task A_follow_up_in_the_same_conversation_continues_it()
    {
        await ResetAsync();
        await SwitchOnAsync();
        var rig = NewRig();
        using var host = rig.Host;
        rig.Model.Script(new ModelTextPart("First."), new ModelFinishPart("stop", 1, 1));
        rig.Model.Script(new ModelTextPart("Second."), new ModelFinishPart("stop", 1, 1));
        var client = await SignedInAsync(host);

        using var first = await PostChatAsync(client, "one");
        var conversation = (await AllFramesAsync(first))[0].Data.GetProperty("conversationId").GetGuid();
        using var second = await PostChatAsync(client, "two", conversation);
        var frames = await AllFramesAsync(second);

        frames[0].Data.GetProperty("conversationId").GetGuid().ShouldBe(conversation);
        (await CountAsync("ai.conversations")).ShouldBe(1);
        (await CountAsync("ai.messages")).ShouldBe(4);
    }

    [Fact]
    public async Task The_stream_sends_a_ping_while_the_model_is_silent_and_goes_on_when_the_model_answers()
    {
        await ResetAsync();
        await SwitchOnAsync();
        var rig = NewRig(b => b.UseSetting("Assistant:Turn:PingSeconds", "1"));
        using var host = rig.Host;
        var release = new TaskCompletionSource();
        rig.Model.Script(token => Silent(release.Task, token));
        var client = await SignedInAsync(host);

        using var response = await PostChatAsync(client, "slow one");
        var names = new List<string>();
        await foreach (var frame in FramesAsync(response))
        {
            names.Add(frame.Event);
            if (frame.Event == "ping")
            {
                release.TrySetResult(); // the model stays silent until the stream has shown a ping
            }
        }

        names.ShouldContain("ping");
        names.Where(e => e != "ping").ShouldBe(["turn", "status", "token", "done"]);
        return;

        static async IAsyncEnumerable<ModelStreamPart> Silent(Task gate, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
        {
            await gate.WaitAsync(token);
            yield return new ModelTextPart("Done.");
            yield return new ModelFinishPart("stop", 1, 1);
        }
    }

    [Fact]
    public async Task A_client_that_goes_away_mid_answer_stores_the_partial_answer_and_frees_the_slot()
    {
        await ResetAsync();
        await SwitchOnAsync();
        var rig = NewRig();
        using var host = rig.Host;
        rig.Model.Script(Endless);
        rig.Model.Script(new ModelTextPart("Back again."), new ModelFinishPart("stop", 1, 1));
        var client = await SignedInAsync(host);

        using (var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct))
        {
            using var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, new Uri("/api/assistant/chat", UriKind.Relative)) { Content = Chat("long one") }, HttpCompletionOption.ResponseHeadersRead, cancel.Token);
            await using var stream = await response.Content.ReadAsStreamAsync(cancel.Token);
            using var reader = new StreamReader(stream);
            var sawToken = false;
            while (!sawToken && await reader.ReadLineAsync(cancel.Token) is { } line)
            {
                sawToken = line == "event: token";
            }

            sawToken.ShouldBeTrue();
            await cancel.CancelAsync();
        }

        await rig.Stored.Task.WaitAsync(Ct); // the turn has inserted its assistant message
        (await CountAsync("ai.messages", "role = 'assistant' AND finish_reason = 'cancelled' AND grounding_status = 'not_checked' AND content LIKE 'partial%'")).ShouldBe(1);
        using var next = await PostChatAsync(client, "again");
        next.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AllFramesAsync(next)).Last().Event.ShouldBe("done");
        return;

        static async IAsyncEnumerable<ModelStreamPart> Endless([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
        {
            yield return new ModelTextPart("partial answer");
            await Task.Delay(Timeout.Infinite, token);
        }
    }

    [Fact]
    public async Task While_a_turn_runs_a_second_one_is_busy_at_once_even_from_another_session()
    {
        await ResetAsync();
        await SwitchOnAsync();
        var rig = NewRig();
        using var host = rig.Host;
        var release = new TaskCompletionSource();
        rig.Model.Script(token => Hold(release.Task, token));
        rig.Model.Script();
        var first = await SignedInAsync(host);
        var second = await SignedInAsync(host);
        using var running = await PostChatAsync(first, "first");

        using var busy = await PostChatAsync(second, "second");

        busy.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeAsync(busy)).ShouldBe("assistant.busy");
        release.SetResult();
        (await AllFramesAsync(running)).Last().Event.ShouldBe("done");
        (await CountAsync("ai.messages", "content = 'second'")).ShouldBe(0);
        return;

        static async IAsyncEnumerable<ModelStreamPart> Hold(Task gate, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
        {
            await gate.WaitAsync(token);
            yield return new ModelTextPart("Done.");
            yield return new ModelFinishPart("stop", 1, 1);
        }
    }

    [Theory]
    [InlineData("model-down", 503, "assistant.model_unavailable")]
    [InlineData("tools-down", 503, "assistant.tools_unavailable")]
    [InlineData("nightly", 409, "assistant.nightly_running")]
    [InlineData("empty", 422, "assistant.invalid")]
    [InlineData("long", 422, "assistant.invalid")]
    [InlineData("nul", 422, "assistant.invalid")]
    [InlineData("unknown-conversation", 404, "assistant.not_found")]
    public async Task A_refusal_is_a_problem_with_a_code_before_any_stream_and_stores_nothing(string situation, int status, string code)
    {
        await ResetAsync();
        await SwitchOnAsync();
        var rig = NewRig();
        using var host = rig.Host;
        if (situation == "model-down")
        {
            rig.Health.State = LlmState.Loading;
        }

        if (situation == "tools-down")
        {
            rig.Tools.State = ToolCatalogState.Down;
        }

        if (situation == "nightly")
        {
            await ExecuteAsync("INSERT INTO platform.service_runs (id, service_id, trigger, attempt, started_at, status) VALUES (gen_random_uuid(), 'nightly', 'scheduled', 1, now() - interval '5 minutes', 'running')");
        }

        var message = situation switch { "empty" => "  ", "long" => new string('a', 2001), "nul" => "a\0b", _ => "hello" };
        var client = await SignedInAsync(host);

        using var response = await PostChatAsync(client, message, situation == "unknown-conversation" ? Guid.NewGuid() : null);

        ((int)response.StatusCode).ShouldBe(status);
        var body = await response.Content.ReadAsStringAsync(Ct);
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("code").GetString().ShouldBe(code);
        if (situation == "model-down")
        {
            json.RootElement.GetProperty("state").GetString().ShouldBe("loading");
        }

        body.ShouldNotContain("Exception");
        (rig.Model.Calls, await AuditRowsAsync()).ShouldBe((0, 0));
    }

    [Fact]
    public async Task The_eleventh_question_within_a_minute_is_rate_limited()
    {
        await ResetAsync();
        var rig = NewRig();
        using var host = rig.Host;
        var client = await SignedInAsync(host);
        var codes = new List<HttpStatusCode>();

        for (var i = 0; i < 11; i++)
        {
            using var response = await PostChatAsync(client, "hello");
            codes.Add(response.StatusCode);
        }

        codes.Take(10).ShouldAllBe(c => c == HttpStatusCode.Conflict);
        codes[10].ShouldBe((HttpStatusCode)429);
    }

    private static Task<HttpResponseMessage> PutSettingsAsync(HttpClient client, object body) =>
        client.PutAsJsonAsync(new Uri("/api/assistant/settings", UriKind.Relative), body, Ct);

    [Fact]
    public async Task Turning_it_on_needs_the_password_for_that_change_and_the_actor_is_the_session_never_the_body()
    {
        await ResetAsync();
        using var host = NewRig().Host;
        var client = await SignedInAsync(host);
        var body = new { enabled = true, portfolioContext = false, maxToolCalls = 6, reason = "start", expectedSettingsId = 0, actor = "owner:evil@example.test", password = (string?)null };

        using var none = await PutSettingsAsync(client, body);
        using var wrong = await PutSettingsAsync(client, body with { password = "not-it" });
        (await CountAsync("ai.settings")).ShouldBe(0);
        using var ok = await PutSettingsAsync(client, body with { password = factory.OwnerPassword });

        none.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CodeAsync(none)).ShouldBe("auth.invalid_credentials");
        wrong.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        ok.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var status = await ok.Content.ReadFromJsonAsync<JsonElement>(Ct);
        (status.GetProperty("enabled").GetBoolean(), status.GetProperty("settingsId").GetInt64()).ShouldBe((true, await ScalarAsync("SELECT max(id) FROM ai.settings")));
        (await CountAsync("ai.settings")).ShouldBe(1);
        (await CountAsync("ai.settings", $"changed_by = '{Owner}' AND reason = 'start' AND length(snapshot_hash) = 64")).ShouldBe(1);
        (await CountAsync("ai.settings", "changed_by LIKE '%evil%'")).ShouldBe(0);
    }

    private Task<long> ScalarAsync(string sql) => factory.WithDbAsync(async db => await db.Database.SqlQueryRaw<long>($"SELECT ({sql}) AS \"Value\"").SingleAsync(Ct));
#pragma warning restore EF1002

    [Fact]
    public async Task Switching_it_off_or_lowering_it_needs_no_password_and_raising_the_budget_does()
    {
        await ResetAsync();
        await ExecuteAsync($"INSERT INTO ai.settings (enabled, portfolio_context, max_tool_calls, changed_by, reason) VALUES (true, true, 8, '{Owner}', 'setup')");
        using var host = NewRig().Host;
        var client = await SignedInAsync(host);
        var current = await ScalarAsync("SELECT max(id) FROM ai.settings");

        using var lower = await PutSettingsAsync(client, new { enabled = true, portfolioContext = true, maxToolCalls = 4, reason = "lower", expectedSettingsId = current });
        var afterLower = (await lower.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("settingsId").GetInt64();
        using var portfolioOff = await PutSettingsAsync(client, new { enabled = true, portfolioContext = false, maxToolCalls = 4, reason = "portfolio off", expectedSettingsId = afterLower });
        var afterPortfolio = (await portfolioOff.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("settingsId").GetInt64();
        using var raise = await PutSettingsAsync(client, new { enabled = true, portfolioContext = false, maxToolCalls = 5, reason = "raise", expectedSettingsId = afterPortfolio });
        using var off = await PutSettingsAsync(client, new { enabled = false, portfolioContext = false, maxToolCalls = 4, reason = "off", expectedSettingsId = afterPortfolio });

        lower.StatusCode.ShouldBe(HttpStatusCode.OK);
        portfolioOff.StatusCode.ShouldBe(HttpStatusCode.OK);
        raise.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        off.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await CountAsync("ai.settings")).ShouldBe(4);
        var status = await client.GetFromJsonAsync<JsonElement>("/api/assistant/status", Ct);
        (status.GetProperty("enabled").GetBoolean(), status.GetProperty("maxToolCalls").GetInt32()).ShouldBe((false, 4));
    }

    [Fact]
    public async Task A_bad_budget_a_blank_reason_and_a_stale_form_are_refused_and_write_nothing()
    {
        await ResetAsync();
        using var host = NewRig().Host;
        var client = await SignedInAsync(host);
        var pw = factory.OwnerPassword;

        using var badBudget = await PutSettingsAsync(client, new { enabled = true, portfolioContext = false, maxToolCalls = 13, reason = "x", expectedSettingsId = 0, password = pw });
        using var blankReason = await PutSettingsAsync(client, new { enabled = true, portfolioContext = false, maxToolCalls = 6, reason = " ", expectedSettingsId = 0, password = pw });
        using var stale = await PutSettingsAsync(client, new { enabled = true, portfolioContext = false, maxToolCalls = 6, reason = "x", expectedSettingsId = 41, password = pw });

        (badBudget.StatusCode, blankReason.StatusCode, stale.StatusCode).ShouldBe((HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.Conflict));
        (await CodeAsync(badBudget)).ShouldBe("assistant.values_invalid");
        (await CodeAsync(blankReason)).ShouldBe("assistant.reason_invalid");
        (await CodeAsync(stale)).ShouldBe("assistant.stale");
        (await CountAsync("ai.settings")).ShouldBe(0);
    }

    [Fact]
    public async Task Wrong_passwords_count_toward_the_lockout_and_a_locked_account_cannot_turn_it_on()
    {
        await ResetAsync();
        await factory.RestoreOwnerAsync();
        try
        {
            using var host = NewRig().Host;
            var client = await SignedInAsync(host);
            var body = new { enabled = true, portfolioContext = false, maxToolCalls = 6, reason = "x", expectedSettingsId = 0, password = "wrong" };
            for (var i = 0; i < 4; i++)
            {
                using var refused = await PutSettingsAsync(client, body);
                refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            }

            using var locking = await PutSettingsAsync(client, body);
            using var duringLock = await PutSettingsAsync(client, body with { password = factory.OwnerPassword });

            locking.StatusCode.ShouldBe((HttpStatusCode)423);
            (await CodeAsync(locking)).ShouldBe("auth.locked");
            duringLock.StatusCode.ShouldBe((HttpStatusCode)423);
            (await CountAsync("ai.settings")).ShouldBe(0);
        }
        finally
        {
            await factory.RestoreOwnerAsync();
        }
    }

    [Fact]
    public async Task A_password_check_for_a_setting_neither_opens_nor_uses_up_the_step_up_window()
    {
        await ResetAsync();
        using var host = NewRig().Host;
        var client = await SignedInAsync(host);
        var elevatedBefore = await factory.WithDbAsync(async db => await db.AuthSessions.AsNoTracking().CountAsync(s => s.ElevatedUntil != null && s.ElevatedUntil > DateTimeOffset.UtcNow, Ct));

        using var ok = await PutSettingsAsync(client, new { enabled = true, portfolioContext = false, maxToolCalls = 6, reason = "x", expectedSettingsId = 0, password = factory.OwnerPassword });

        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await factory.WithDbAsync(async db => await db.AuthSessions.AsNoTracking().CountAsync(s => s.ElevatedUntil != null && s.ElevatedUntil > DateTimeOffset.UtcNow, Ct))).ShouldBe(elevatedBefore);
    }

    [Fact]
    public async Task The_reads_answer_404_for_unknown_ids_and_clamp_the_limit()
    {
        await ResetAsync();
        using var host = NewRig().Host;
        var client = await SignedInAsync(host);

        using var conversation = await client.GetAsync($"/api/assistant/conversations/{Guid.NewGuid()}", Ct);
        using var call = await client.GetAsync("/api/assistant/tool-calls/999999", Ct);
        using var badGuid = await client.GetAsync("/api/assistant/conversations/not-a-guid", Ct);
        using var list = await client.GetAsync("/api/assistant/conversations?limit=500", Ct);

        conversation.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(conversation)).ShouldBe("assistant.not_found");
        call.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        badGuid.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        list.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await list.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task With_the_model_and_the_tool_server_unreachable_the_api_stays_healthy_and_the_status_is_honest()
    {
        await ResetAsync();
        using var host = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Assistant:Llm:Enabled", "true");
            b.UseSetting("Assistant:Llm:ApiKey", "integration-test-key-0123456789abcdef");
            b.UseSetting("Assistant:Llm:BaseUrl", "http://127.0.0.1:1/v1");
            b.UseSetting("Assistant:Mcp:Url", "http://127.0.0.1:1/mcp");
            b.UseSetting("Assistant:Mcp:Token", "integration-test-token-0123456789");
        });
        await SwitchOnAsync();
        var client = await SignedInAsync(host);

        using var ready = await host.CreateClient().GetAsync(new Uri("/health/ready", UriKind.Relative), Ct);
        using var status = await client.GetAsync("/api/assistant/status", Ct);
        using var chat = await PostChatAsync(client, "hello");

        ready.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await status.Content.ReadFromJsonAsync<JsonElement>(Ct);
        json.GetProperty("llm").GetProperty("state").GetString().ShouldBe("down");
        json.GetProperty("tools").GetProperty("state").GetString().ShouldBe("down");
        chat.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await CodeAsync(chat)).ShouldBe("assistant.model_unavailable");
        (await AuditRowsAsync()).ShouldBe(0);
    }
}

/// <summary>The real store, telling a test when an assistant message has been inserted (so that it need not wait on a clock).</summary>
internal sealed class SignallingStore(IConversationStore inner, TaskCompletionSource stored) : IConversationStore
{
    public ValueTask<bool> ExistsAsync(Guid conversationId, CancellationToken cancellationToken) => inner.ExistsAsync(conversationId, cancellationToken);

    public ValueTask<IReadOnlyList<HistoryMessage>> RecentAsync(Guid conversationId, int count, CancellationToken cancellationToken) => inner.RecentAsync(conversationId, count, cancellationToken);

    public ValueTask<StartedTurn> StartTurnAsync(StartTurnRecord record, CancellationToken cancellationToken) => inner.StartTurnAsync(record, cancellationToken);

    public ValueTask<long> RecordToolCallAsync(ToolCallRecord record, CancellationToken cancellationToken) => inner.RecordToolCallAsync(record, cancellationToken);

    public async ValueTask<long> CompleteTurnAsync(AssistantMessageRecord record, CancellationToken cancellationToken)
    {
        var id = await inner.CompleteTurnAsync(record, cancellationToken);
        stored.TrySetResult();
        return id;
    }
}
