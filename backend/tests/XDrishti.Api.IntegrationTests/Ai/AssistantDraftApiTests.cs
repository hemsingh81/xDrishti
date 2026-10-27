using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Ai.Drafting;

namespace XDrishti.Api.IntegrationTests.Ai;

/// <summary>
/// Spec P9-07 end to end on the real API with a scripted model: off by default, the job stores a draft that the real Lab validator accepted, nothing becomes a strategy until the owner confirms with the switch on and a fresh password, a second confirm repeats the first
/// result, and the actor is the session. No real model is involved (UNVERIFIED with one).
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class AssistantDraftApiTests(ApiFactory factory)
{
    private const string Owner = "owner:" + ApiFactory.OwnerEmail;

    private const string Idea = "Intraday: buy when a 15m candle closes above VWAP after a pullback of 0.5 ATR(14), between 10:00 and 14:30, exit by 15:15, target 1:2.";

    private const string Seed = """
        strategy:
          schema: 1
          id: __ID__
          style: intraday
          timeframes:
            - 15m
          sides:
            - BUY
          params:
            pull_atr: 0.5
            rr: 2
          setup:
            all:
              - "tf(\"1h\").ema(20) > tf(\"1h\").ema(50)"
          trigger:
            all:
              - "close > vwap and close > open"
              - "low[1] <= vwap[1] + pull_atr * atr(14)"
          entry:
            type: at_close
          stop:
            type: swing_low
            lookback: 2
          exits:
            - policy: FIXED_RR
              rr: [rr]
          time_rules:
            first_entry: "10:00"
            last_entry: "14:30"
            square_off: "15:15"
        """;

    private static readonly (HttpMethod Method, string Path)[] Routes =
    [
        (HttpMethod.Get, "/api/assistant/strategy-drafts"), (HttpMethod.Get, "/api/assistant/strategy-drafts/1"), (HttpMethod.Post, "/api/assistant/strategy-drafts"), (HttpMethod.Post, "/api/assistant/strategy-drafts/1/confirm"),
        (HttpMethod.Post, "/api/assistant/strategy-drafts/1/discard"),
    ];

    private static readonly string[] NoStrings = [];

    private static readonly string[] Understood = ["buy above VWAP after a pullback of 0.5 ATR(14)"];

    private static readonly object[] Assumptions = [new { field = "params.rr", value = "2", why = "target 1:2" }];

    private static readonly string[] Unknown = ["NOT_A_REAL_SYMBOL_XYZ"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string NewId() => "D" + Guid.NewGuid().ToString("N")[..12];

    private static string Answer(string id) => JsonSerializer.Serialize(new
    {
        decline = false,
        unsupported = NoStrings,
        understood = Understood,
        assumptions = Assumptions,
        not_expressed = NoStrings,
        requested_test = new { from = (string?)null, to = (string?)null, baskets = NoStrings },
        yaml = Seed.Replace("__ID__", id, StringComparison.Ordinal).ReplaceLineEndings("\n") + "\n",
    });

    private (WebApplicationFactory<Program> Host, ScriptedModel Model) NewHost(bool enabled)
    {
        var model = new ScriptedModel();
        var host = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Assistant:Drafts:Enabled", enabled ? "true" : "false");
            b.ConfigureTestServices(services =>
            {
                services.RemoveAll<IChatModel>().AddSingleton<IChatModel>(model);
                services.RemoveAll<ILlmHealth>().AddSingleton<ILlmHealth>(new ScriptedHealth());
            });
        });
        return (host, model);
    }

    private async Task<(HttpClient Client, string Password)> SignedInAsync(WebApplicationFactory<Program> host)
    {
        using var login = await factory.CreateAnonymousClient().LoginAsync(ApiFactory.OwnerEmail, factory.OwnerPassword, Ct);
        var session = (await login.Content.ReadFromJsonAsync<XDrishti.Application.Auth.SessionInfo>(Ct))!;
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", $"198.19.{Random.Shared.Next(1, 250)}.{Random.Shared.Next(1, 250)}");
        client.DefaultRequestHeaders.Add("Cookie", $"xd.sid={ApiFactory.SessionCookieValue(login)}");
        client.DefaultRequestHeaders.Add("X-CSRF-Token", session.CsrfToken);
        client.Timeout = TimeSpan.FromSeconds(60);
        return (client, factory.OwnerPassword);
    }

    private Task<int> ExecuteAsync(string sql) => factory.WithDbAsync(async db => await db.Database.ExecuteSqlRawAsync(sql, Ct));

    private Task<int> SettingsAsync(bool enabled, bool allowConfirm, bool autoQuick = false) =>
        ExecuteAsync($"DELETE FROM ai.settings; INSERT INTO ai.settings (enabled, portfolio_context, max_tool_calls, changed_by, reason, allow_draft_confirm, auto_lab_quick_test) VALUES ({(enabled ? "true" : "false")}, false, 6, '{Owner}', 'test setup', {(allowConfirm ? "true" : "false")}, {(autoQuick ? "true" : "false")})");

    private static string BasketName() => "Bk" + new string([.. Guid.NewGuid().ToByteArray().Take(10).Select(b => (char)('a' + (b % 26)))]);

    private static async Task<long> StartNamingAsync(HttpClient client, WebApplicationFactory<Program> host, string idea)
    {
        using var response = await client.PostAsJsonAsync(new Uri("/api/assistant/strategy-drafts", UriKind.Relative), new { idea }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetInt64();
        await host.Services.GetRequiredService<DraftJobs>().WhenIdleAsync();
        return id;
    }

#pragma warning disable EF1002 // the table names and conditions are the constants of these tests
    private async Task<long> CountAsync(string table, string where = "true") =>
        await factory.WithDbAsync(async db => await db.Database.SqlQueryRaw<long>($"SELECT count(*) AS \"Value\" FROM {table} WHERE {where}").SingleAsync(Ct));
#pragma warning restore EF1002

    private static async Task<JsonElement> DraftAsync(HttpClient client, long id) =>
        await client.GetFromJsonAsync<JsonElement>(new Uri($"/api/assistant/strategy-drafts/{id}", UriKind.Relative), Ct);

    private static async Task<long> StartAsync(HttpClient client, WebApplicationFactory<Program> host)
    {
        using var response = await client.PostAsJsonAsync(new Uri("/api/assistant/strategy-drafts", UriKind.Relative), new { idea = Idea }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        json.GetProperty("status").GetString().ShouldBe("drafting");
        await host.Services.GetRequiredService<DraftJobs>().WhenIdleAsync();
        return json.GetProperty("id").GetInt64();
    }

    [Fact]
    public async Task Every_route_needs_a_session_and_the_writes_need_the_token()
    {
        var anonymous = factory.CreateAnonymousClient();
        foreach (var (method, path) in Routes)
        {
            using var response = await anonymous.SendAsync(new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = method == HttpMethod.Get ? null : JsonContent.Create(new { }) }, Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, path);
        }

        var signedIn = await factory.SignInAsync(Ct);
        var noToken = factory.CreateAnonymousClient();
        noToken.DefaultRequestHeaders.Add("Cookie", signedIn.CookieHeader);
        using var post = await noToken.PostAsJsonAsync(new Uri("/api/assistant/strategy-drafts", UriKind.Relative), new { idea = Idea }, Ct);
        post.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task By_default_drafting_is_off_and_nothing_is_stored_or_called()
    {
        await SettingsAsync(enabled: true, allowConfirm: false);
        var (host, model) = NewHost(enabled: false);
        using var _ = host;
        var (client, _) = await SignedInAsync(host);
        var before = await CountAsync("ai.strategy_drafts");

        using var off = await client.PostAsJsonAsync(new Uri("/api/assistant/strategy-drafts", UriKind.Relative), new { idea = Idea }, Ct);
        using var invalid = await client.PostAsJsonAsync(new Uri("/api/assistant/strategy-drafts", UriKind.Relative), new { idea = "short" }, Ct);

        off.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await off.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("assistant.disabled");
        invalid.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        model.Calls.ShouldBe(0);
        (await CountAsync("ai.strategy_drafts")).ShouldBe(before);
    }

    [Fact]
    public async Task The_master_switch_of_the_assistant_is_also_needed()
    {
        await SettingsAsync(enabled: false, allowConfirm: false);
        var (host, model) = NewHost(enabled: true);
        using var _ = host;
        var (client, _) = await SignedInAsync(host);

        using var response = await client.PostAsJsonAsync(new Uri("/api/assistant/strategy-drafts", UriKind.Relative), new { idea = Idea }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        model.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_draft_is_made_validated_by_the_real_lab_validator_and_stores_nothing_as_a_strategy()
    {
        await SettingsAsync(enabled: true, allowConfirm: false);
        var (host, model) = NewHost(enabled: true);
        using var _ = host;
        var (client, _) = await SignedInAsync(host);
        var id = NewId();
        model.Script(new ModelTextPart(Answer(id)), new ModelFinishPart("stop", 100, 50));
        var strategiesBefore = await CountAsync("lab.strategies");

        var draftId = await StartAsync(client, host);
        var draft = await DraftAsync(client, draftId);

        draft.GetProperty("status").GetString().ShouldBe("valid");
        draft.GetProperty("attempts").GetInt32().ShouldBe(1);
        draft.GetProperty("findings").GetArrayLength().ShouldBe(0);
        draft.GetProperty("yaml").GetString()!.ShouldContain(id);
        draft.GetProperty("understood")[0].GetString()!.ShouldContain("VWAP");
        (await CountAsync("lab.strategies")).ShouldBe(strategiesBefore);
        (await CountAsync("ai.model_calls", "purpose = 'draft'")).ShouldBeGreaterThan(0);
        (await CountAsync("ai.strategy_draft_decisions", $"draft_id = {draftId}")).ShouldBe(0);
        using var list = await client.GetAsync(new Uri("/api/assistant/strategy-drafts?limit=3", UriKind.Relative), Ct);
        (await list.Content.ReadFromJsonAsync<JsonElement>(Ct)).EnumerateArray().First().GetProperty("id").GetInt64().ShouldBe(draftId);
    }

    [Fact]
    public async Task Confirm_is_refused_while_the_switch_is_off_without_counting_a_password_try_and_works_with_the_switch_and_a_fresh_password()
    {
        await SettingsAsync(enabled: true, allowConfirm: false);
        var (host, model) = NewHost(enabled: true);
        using var _ = host;
        var (client, password) = await SignedInAsync(host);
        var id = NewId();
        model.Script(new ModelTextPart(Answer(id)), new ModelFinishPart("stop", 100, 50));
        var draftId = await StartAsync(client, host);
        var confirm = new Uri($"/api/assistant/strategy-drafts/{draftId}/confirm", UriKind.Relative);

        using var off = await client.PostAsJsonAsync(confirm, new { run = false, password = "wrong-on-purpose" }, Ct);
        await SettingsAsync(enabled: true, allowConfirm: true);
        using var wrong = await client.PostAsJsonAsync(confirm, new { run = false, password = "wrong-on-purpose" }, Ct);
        using var missing = await client.PostAsJsonAsync(confirm, new { run = false }, Ct);
        (await CountAsync("lab.strategies", $"id = '{id}'")).ShouldBe(0);
        using var ok = await client.PostAsJsonAsync(confirm, new { run = false, password }, Ct);
        using var again = await client.PostAsJsonAsync(confirm, new { run = false, password }, Ct);

        off.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await off.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("assistant.capability_off");
        wrong.StatusCode.ShouldNotBe(HttpStatusCode.OK);
        missing.StatusCode.ShouldNotBe(HttpStatusCode.OK);
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        var confirmed = (await ok.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("confirmed");
        (confirmed.GetProperty("strategyId").GetString(), confirmed.GetProperty("version").GetInt32(), confirmed.GetProperty("edited").GetBoolean()).ShouldBe((id, 1, false));
        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await CountAsync("lab.strategies", $"id = '{id}'")).ShouldBe(1);
        (await CountAsync("lab.strategy_versions", $"strategy_id = '{id}'")).ShouldBe(1);
        (await CountAsync("ai.strategy_draft_decisions", $"draft_id = {draftId} AND actor = '{Owner}' AND decision = 'confirmed' AND NOT auto")).ShouldBe(1);
        (await DraftAsync(client, draftId)).GetProperty("status").GetString().ShouldBe("confirmed");
        using var discard = await client.PostAsync(new Uri($"/api/assistant/strategy-drafts/{draftId}/discard", UriKind.Relative), null, Ct);
        discard.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Two_concurrent_confirms_of_one_draft_store_one_version_and_two_drafts_with_one_id_cannot_both_confirm()
    {
        await SettingsAsync(enabled: true, allowConfirm: true);
        var (host, model) = NewHost(enabled: true);
        using var _ = host;
        var (client, password) = await SignedInAsync(host);
        var id = NewId();
        for (var i = 0; i < 3; i++)
        {
            model.Script(new ModelTextPart(Answer(id)), new ModelFinishPart("stop", 100, 50));
        }

        var one = await StartAsync(client, host);
        var two = await StartAsync(client, host);
        var three = await StartAsync(client, host);
        Task<HttpResponseMessage> Confirm(long draft) => client.PostAsJsonAsync(new Uri($"/api/assistant/strategy-drafts/{draft}/confirm", UriKind.Relative), new { run = false, password }, Ct);

        var same = await Task.WhenAll(Confirm(one), Confirm(one));
        var other = await Task.WhenAll(Confirm(two), Confirm(three));

        same.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK);
        other.Select(r => r.StatusCode).Order().ShouldBe([HttpStatusCode.Conflict, HttpStatusCode.Conflict]);
        (await CountAsync("lab.strategy_versions", $"strategy_id = '{id}'")).ShouldBe(1);
        (await CountAsync("ai.strategy_draft_decisions", $"draft_id IN ({one}, {two}, {three})")).ShouldBe(1);
        foreach (var r in same.Concat(other))
        {
            r.Dispose();
        }
    }

    [Fact]
    public async Task A_quick_test_that_cannot_start_still_leaves_the_stored_version_and_a_text_the_owner_edited_is_what_is_stored()
    {
        await SettingsAsync(enabled: true, allowConfirm: true);
        var (host, model) = NewHost(enabled: true);
        using var _ = host;
        var (client, password) = await SignedInAsync(host);
        var id = NewId();
        model.Script(new ModelTextPart(Answer(id)), new ModelFinishPart("stop", 100, 50));
        var draftId = await StartAsync(client, host);
        var edited = Seed.Replace("__ID__", id, StringComparison.Ordinal).Replace("rr: 2", "rr: 3", StringComparison.Ordinal).ReplaceLineEndings("\n") + "\n";

        using var response = await client.PostAsJsonAsync(new Uri($"/api/assistant/strategy-drafts/{draftId}/confirm", UriKind.Relative), new { text = edited, symbols = Unknown, run = true, password }, Ct);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        json.GetProperty("confirmed").GetProperty("edited").GetBoolean().ShouldBeTrue();
        json.GetProperty("confirmed").GetProperty("labRunId").ValueKind.ShouldBe(JsonValueKind.Null);
        json.GetProperty("confirmed").GetProperty("runError").GetString().ShouldStartWith("lab.");
        (await CountAsync("lab.strategy_versions", $"strategy_id = '{id}' AND note = 'Assistant draft {draftId}; confirmed by owner' AND source_text LIKE '%rr: 3%'")).ShouldBe(1);
    }

    [Fact]
    public async Task A_taken_id_an_invalid_edit_and_a_missing_universe_are_refused_and_store_nothing()
    {
        await SettingsAsync(enabled: true, allowConfirm: true);
        var (host, model) = NewHost(enabled: true);
        using var _ = host;
        var (client, password) = await SignedInAsync(host);
        var id = NewId();
        model.Script(new ModelTextPart(Answer(id)), new ModelFinishPart("stop", 100, 50));
        model.Script(new ModelTextPart(Answer(id)), new ModelFinishPart("stop", 100, 50));
        var firstId = await StartAsync(client, host);
        var draftId = await StartAsync(client, host);
        using var first = await client.PostAsJsonAsync(new Uri($"/api/assistant/strategy-drafts/{firstId}/confirm", UriKind.Relative), new { run = false, password }, Ct);
        var confirm = new Uri($"/api/assistant/strategy-drafts/{draftId}/confirm", UriKind.Relative);

        using var noUniverse = await client.PostAsJsonAsync(confirm, new { run = true, password }, Ct);
        using var taken = await client.PostAsJsonAsync(confirm, new { run = false, password }, Ct);
        using var bad = await client.PostAsJsonAsync(confirm, new { text = "strategy:\n  id: Other\n", run = false, password }, Ct);

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        noUniverse.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        taken.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await taken.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("assistant.draft_id_taken");
        bad.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await bad.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("problems").GetArrayLength().ShouldBeGreaterThan(0);
        (await CountAsync("ai.strategy_draft_decisions", $"draft_id = {draftId}")).ShouldBe(0);
    }

    [Fact]
    public async Task A_discard_is_a_decision_and_a_declined_idea_is_not_confirmable()
    {
        await SettingsAsync(enabled: true, allowConfirm: true);
        var (host, model) = NewHost(enabled: true);
        using var _ = host;
        var (client, password) = await SignedInAsync(host);
        model.Script(new ModelTextPart("""{"decline":true,"unsupported":["news and events data"],"yaml":null}"""), new ModelFinishPart("stop", 10, 10));
        var draftId = await StartAsync(client, host);

        var draft = await DraftAsync(client, draftId);
        using var confirm = await client.PostAsJsonAsync(new Uri($"/api/assistant/strategy-drafts/{draftId}/confirm", UriKind.Relative), new { run = false, password }, Ct);
        using var discard = await client.PostAsync(new Uri($"/api/assistant/strategy-drafts/{draftId}/discard", UriKind.Relative), null, Ct);

        draft.GetProperty("status").GetString().ShouldBe("declined");
        draft.GetProperty("unsupported")[0].GetString()!.ShouldContain("news");
        draft.GetProperty("yaml").ValueKind.ShouldBe(JsonValueKind.Null);
        confirm.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await confirm.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("assistant.draft_invalid");
        discard.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await DraftAsync(client, draftId)).GetProperty("status").GetString().ShouldBe("discarded");
        using var missing = await client.GetAsync(new Uri("/api/assistant/strategy-drafts/999999999", UriKind.Relative), Ct);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Enabling_the_auto_quick_test_needs_the_password_the_other_inert_switches_stay_refused_and_turning_it_off_is_free()
    {
        await SettingsAsync(enabled: true, allowConfirm: false);
        var (host, _) = NewHost(enabled: true);
        using var _ = host;
        var (client, password) = await SignedInAsync(host);
        var settings = new Uri("/api/assistant/settings", UriKind.Relative);
        async Task<long> CurrentIdAsync() => (await client.GetFromJsonAsync<JsonElement>(new Uri("/api/assistant/status", UriKind.Relative), Ct)).GetProperty("settingsId").GetInt64();
        object Body(long expected, string? pw, bool? auto = null, bool? proposal = null, bool? hypothesis = null) =>
            new { enabled = true, portfolioContext = false, maxToolCalls = 6, reason = "auto test", expectedSettingsId = expected, password = pw, autoLabQuickTest = auto, allowProposalAccept = proposal, autoAcceptHypothesis = hypothesis };
        var rows = await CountAsync("ai.settings");

        using var noPassword = await client.PutAsJsonAsync(settings, Body(await CurrentIdAsync(), null, auto: true), Ct);
        using var wrong = await client.PutAsJsonAsync(settings, Body(await CurrentIdAsync(), "wrong-on-purpose", auto: true), Ct);
        (await CountAsync("ai.settings")).ShouldBe(rows);
        using var proposalOn = await client.PutAsJsonAsync(settings, Body(await CurrentIdAsync(), password, proposal: true), Ct);
        using var hypothesisOn = await client.PutAsJsonAsync(settings, Body(await CurrentIdAsync(), password, hypothesis: true), Ct);
        using var on = await client.PutAsJsonAsync(settings, Body(await CurrentIdAsync(), password, auto: true), Ct);
        var onStatus = await on.Content.ReadFromJsonAsync<JsonElement>(Ct);
        using var off = await client.PutAsJsonAsync(settings, Body(onStatus.GetProperty("settingsId").GetInt64(), null, auto: false), Ct);

        noPassword.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        wrong.StatusCode.ShouldNotBe(HttpStatusCode.OK);
        proposalOn.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await proposalOn.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("assistant.setting_not_available");
        hypothesisOn.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        on.StatusCode.ShouldBe(HttpStatusCode.OK);
        onStatus.GetProperty("autoLabQuickTest").GetBoolean().ShouldBeTrue();
        off.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await off.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("autoLabQuickTest").GetBoolean().ShouldBeFalse();
        (await CountAsync("ai.settings", $"changed_by = '{Owner}' AND auto_lab_quick_test AND length(snapshot_hash) = 64")).ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task With_the_auto_option_on_a_clean_draft_naming_one_basket_is_stored_and_started_as_auto_and_the_next_one_waits_for_the_owner()
    {
        await SettingsAsync(enabled: true, allowConfirm: false, autoQuick: true);
        var (host, model) = NewHost(enabled: true);
        using var _ = host;
        var (client, _) = await SignedInAsync(host);
        var name = BasketName();
        using var basket = await client.PostAsJsonAsync(new Uri("/api/baskets", UriKind.Relative), new { name, colour = (string?)null, purpose = (string?)null, description = (string?)null }, Ct);
        basket.StatusCode.ShouldBeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        var first = NewId();
        var second = NewId();
        model.Script(new ModelTextPart(Answer(first)), new ModelFinishPart("stop", 100, 50));
        model.Script(new ModelTextPart(Answer(second)), new ModelFinishPart("stop", 100, 50));

        var one = await StartNamingAsync(client, host, $"{Idea} Test it on {name}.");
        var two = await StartNamingAsync(client, host, $"{Idea} Test it on {name}.");

        var auto = await DraftAsync(client, one);
        auto.GetProperty("status").GetString().ShouldBe("confirmed");
        auto.GetProperty("confirmed").GetProperty("auto").GetBoolean().ShouldBeTrue();
        (await CountAsync("lab.strategy_versions", $"strategy_id = '{first}' AND note LIKE '%auto-confirmed%'")).ShouldBe(1);
        (await CountAsync("ai.strategy_draft_decisions", $"draft_id = {one} AND auto AND actor = 'system:auto-draft' AND settings_id IS NOT NULL AND length(snapshot_hash) = 64")).ShouldBe(1);
        (await CountAsync("lab.lab_runs", $"strategy_id = '{first}' AND method <> 'quick'")).ShouldBe(0);
        (await DraftAsync(client, two)).GetProperty("status").GetString().ShouldBe("valid");
        (await CountAsync("lab.strategy_versions", $"strategy_id = '{second}'")).ShouldBe(0);
        (await CountAsync("ai.strategy_draft_decisions", $"draft_id = {two}")).ShouldBe(0);
    }

    [Fact]
    public async Task With_the_auto_option_off_nothing_is_stored_even_for_a_clean_draft_naming_a_basket()
    {
        await SettingsAsync(enabled: true, allowConfirm: false);
        var (host, model) = NewHost(enabled: true);
        using var _ = host;
        var (client, _) = await SignedInAsync(host);
        var name = BasketName();
        using var basket = await client.PostAsJsonAsync(new Uri("/api/baskets", UriKind.Relative), new { name, colour = (string?)null, purpose = (string?)null, description = (string?)null }, Ct);
        var id = NewId();
        model.Script(new ModelTextPart(Answer(id)), new ModelFinishPart("stop", 100, 50));

        var draft = await StartNamingAsync(client, host, $"{Idea} Test it on {name}.");

        (await DraftAsync(client, draft)).GetProperty("status").GetString().ShouldBe("valid");
        (await CountAsync("lab.strategy_versions", $"strategy_id = '{id}'")).ShouldBe(0);
    }

    [Fact]
    public async Task The_model_being_down_is_an_honest_failed_state()
    {
        await SettingsAsync(enabled: true, allowConfirm: false);
        var (host, model) = NewHost(enabled: true);
        using var _ = host;
        var (client, _) = await SignedInAsync(host);
        model.Script(_ => throw new ModelUnavailableException("down"));

        var draft = await DraftAsync(client, await StartAsync(client, host));

        draft.GetProperty("status").GetString().ShouldBe("failed");
        draft.GetProperty("failReason").GetString().ShouldBe("model_unavailable");
    }
}
