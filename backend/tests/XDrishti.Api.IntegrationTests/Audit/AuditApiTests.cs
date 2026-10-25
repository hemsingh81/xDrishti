using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Learning;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Planning;

namespace XDrishti.Api.IntegrationTests.Audit;

/// <summary>Spec P8-22 (ADR 0117) on a real database with all six stores filled, some by the real writers: the merged read, paging, redaction, the field policy, the refusals and that a read writes nothing.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class AuditApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private const string Wide = "from=2026-10-01&to=2027-09-01";

    private static readonly Guid AccountId = Guid.Parse("0b6f2a9e-7a52-4d7e-9c3a-5d0a7e0b7a40");

    private static readonly string[] SourceCodes = ["auth", "account", "approval", "plan", "autonomy", "learning"];

    private static readonly string[] ForbiddenNames = ["ip", "userAgent", "confirmReason", "modifications", "settingsHash", "snapshotHash", "password", "passwordHash", "token", "sessionId", "csrfToken", "clientId", "cutoff", "policyVersion"];

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "DELETE FROM research.learning_changelog");
        await ExecuteAsync(connection, "DELETE FROM auth.audit_events");
        await ExecuteAsync(
            connection,
            """
            INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget)
            VALUES (@id, 'probe-audit', 'Audit probe', 'Dhan', '1000000140', current_date, '#1A2B3C', now(), now(), false, false, false, 200)
            """,
            ("id", AccountId));
        await ExecuteAsync(connection, "INSERT INTO auth.audit_events (id, at, event, email, ip, user_agent, detail) VALUES (@id, @at, 'LoginFailed', 'hunter2@stranger.invalid', '203.0.113.9', 'probe-agent/1', 'bad password for the address')", ("id", Guid.CreateVersion7()), ("at", Now.AddMinutes(-5)));
        await ExecuteAsync(connection, "INSERT INTO accounts.account_events (id, at, account_id, event, detail, actor) VALUES (@id, @at, @account, 'HoldingsImported', '5 holdings imported', @actor)", ("id", Guid.CreateVersion7()), ("at", Now.AddMinutes(-4)), ("account", AccountId), ("actor", ApiFactory.OwnerEmail));
        await ExecuteAsync(connection, "INSERT INTO accounts.account_events (id, at, account_id, event, detail, actor) VALUES (@id, @at, NULL, 'DataUpdated', 'set password=hunter2 and token: abc123 and client_id=1100000001', 'system:test')", ("id", Guid.CreateVersion7()), ("at", Now.AddMinutes(-3)));
        await ExecuteAsync(connection, "INSERT INTO workflow.autonomy_settings (auto_approve_tickets, promote_from_reserve, min_p_win, max_auto_per_day, actor, reason) VALUES (false, false, 0.6, 3, @actor, 'switches checked')", ("actor", "owner:" + ApiFactory.OwnerEmail));
        using (var scope = Factory.Services.CreateScope())
        {
            var moved = await scope.ServiceProvider.GetRequiredService<ICommandHandler<RecordCellTransitionCommand, ChangelogOutcome>>()
                .HandleAsync(new RecordCellTransitionCommand("ORB|15m|BUY|intraday|FIXED_RR|*", CellState.Candidate, "ready for paper", "cli:test", new TransitionFacts(true, true, true, true, 20), ["run:abc"]), Ct);
            moved.IsSuccess.ShouldBeTrue();
        }

        var item = new NewPlanItem(
            1, TicketGroup.Primary, "NSE:RELIANCE", "BUY", "ORB", new string('a', 64), "close above 15m high", "0.8 ATR below entry", "2R", "09:30-11:30", [],
            new PlanEvidence("ORB|15m|BUY|intraday|FIXED_RR|*", 41, "about 2.1 years, replayed history, modelled fills, no hold-out", "shrunk posterior win rate of the cell", 0.72, 0.12, ["allocation_entry_is_indicative"], null, 0.7),
            [new NewAllocation(AccountId, "paper", 10)]);
        var plan = new NewPlan("audit-2099-01-05", new DateOnly(2099, 1, 5), new DateTimeOffset(2099, 1, 5, 3, 35, 0, TimeSpan.Zero), 1, new string('c', 64), "cell statistics only", true, "test", DateTimeOffset.UnixEpoch, [item]);
        using var planScope = Factory.Services.CreateScope();
        var store = planScope.ServiceProvider.GetRequiredService<IPlanStore>();
        var published = await store.PublishAsync(plan, Ct);
        var itemId = (await store.ItemsAsync(published.Id, Ct))[0].Id;
        using var approve = await Client.PostAsJsonAsync(new Uri($"/api/plans/{published.Id}/items/{itemId}/approve", UriKind.Relative), new { reason = "looks fine, Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.abcdefghijk" }, Ct);
        approve.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task<(string Body, JsonElement Json)> GetAsync(string path)
    {
        using var response = await Client.GetAsync(new Uri(path, UriKind.Relative), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var body = await response.Content.ReadAsStringAsync(Ct);
        return (body, JsonDocument.Parse(body).RootElement.Clone());
    }

    private static void Names(JsonElement element, List<string> into)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    into.Add(property.Name);
                    Names(property.Value, into);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Names(item, into);
                }

                break;
            default:
                break;
        }
    }

    [Theory]
    [InlineData("/api/audit/entries")]
    [InlineData("/api/audit/entries/auth:0b6f2a9e-7a52-4d7e-9c3a-5d0a7e0b7a40")]
    [InlineData("/api/audit/summary")]
    [InlineData("/api/audit/kinds")]
    public async Task Without_a_session_every_audit_read_is_401(string path)
    {
        using var response = await Factory.CreateAnonymousClient().GetAsync(new Uri(path, UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task All_six_stores_are_merged_newest_first_and_the_list_follows_the_field_policy()
    {
        var (body, json) = await GetAsync($"/api/audit/entries?{Wide}&limit=200");

        json.GetProperty("sources").EnumerateArray().Select(s => s.GetProperty("source").GetString()).ShouldBe(SourceCodes);
        json.GetProperty("sources").EnumerateArray().ShouldAllBe(s => s.GetProperty("state").GetString() == "value");
        json.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("source").GetString()).Distinct().Order().ShouldBe(SourceCodes.Order());
        var times = json.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("at").GetDateTimeOffset()).ToList();
        times.ShouldBe([.. times.OrderByDescending(t => t)]);
        var names = new List<string>();
        Names(json, names);
        names.Intersect(ForbiddenNames).ShouldBeEmpty();
        body.ShouldNotContain("203.0.113.9");
        body.ShouldNotContain("probe-agent");
        body.ShouldNotContain("hunter2");
        body.ShouldNotContain("abc123");
        body.ShouldNotContain("1100000001");
        body.ShouldNotContain("eyJhbGci");
        body.ShouldNotContain("stranger.invalid");
        body.ShouldContain("5 holdings imported");
        var failed = json.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("summary").GetString()!.Contains("bad password for the address", StringComparison.Ordinal));
        failed.GetProperty("actorKind").GetString().ShouldBe("other_address");
        failed.GetProperty("outcome").GetString().ShouldBe("failed");
        var secret = json.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("kind").GetString() == "DataUpdated");
        secret.GetProperty("redacted").GetBoolean().ShouldBeTrue();
        json.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("kind").GetString() == "HoldingsImported").GetProperty("accountName").GetString().ShouldBe("Audit probe");
        json.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("source").GetString() == "approval").GetProperty("actorKind").GetString().ShouldBe("owner");
    }

    [Fact]
    public async Task Paging_by_cursor_gives_each_row_once()
    {
        var (_, all) = await GetAsync($"/api/audit/entries?{Wide}&limit=200");
        var expected = all.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()).ToList();
        var seen = new List<string?>();
        string? cursor = null;
        while (cursor is not null || seen.Count == 0)
        {
            var (_, page) = await GetAsync($"/api/audit/entries?{Wide}&limit=2" + (cursor is null ? string.Empty : "&cursor=" + Uri.EscapeDataString(cursor)));
            seen.AddRange(page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()));
            cursor = page.GetProperty("nextCursor").GetString();
            if (seen.Count >= expected.Count)
            {
                break;
            }
        }

        seen.Take(expected.Count).ShouldBe(expected);
        seen.Distinct().Count().ShouldBe(seen.Count);
    }

    [Fact]
    public async Task Filters_narrow_and_the_search_never_finds_what_is_hidden()
    {
        var (_, byKind) = await GetAsync($"/api/audit/entries?{Wide}&kind=HoldingsImported");
        var (_, byAccount) = await GetAsync($"/api/audit/entries?{Wide}&account={AccountId}");
        var (_, byActor) = await GetAsync($"/api/audit/entries?{Wide}&actor=cli");
        var (_, hidden) = await GetAsync($"/api/audit/entries?{Wide}&q=hunter");
        var (_, masked) = await GetAsync($"/api/audit/entries?{Wide}&q=redacted");
        var (_, sourceOnly) = await GetAsync($"/api/audit/entries?{Wide}&source=autonomy,learning");

        byKind.GetProperty("items").GetArrayLength().ShouldBe(1);
        byAccount.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("source").GetString()).ShouldBe(["account"]);
        byActor.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("source").GetString()).ShouldBe(["learning"]);
        hidden.GetProperty("items").GetArrayLength().ShouldBe(0);
        masked.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("kind").GetString()).ShouldContain("DataUpdated");
        sourceOnly.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("source").GetString()).Distinct().Order().ShouldBe(["autonomy", "learning"]);
    }

    [Theory]
    [InlineData("limit=0")]
    [InlineData("limit=201")]
    [InlineData("source=nope")]
    [InlineData("outcome=nope")]
    [InlineData("q=ab")]
    [InlineData("cursor=garbage")]
    [InlineData("from=2024-01-01&to=2026-01-01")]
    [InlineData("from=2026-10-05&to=2026-10-01")]
    public async Task A_bad_parameter_is_422_audit_invalid(string query)
    {
        using var response = await Client.GetAsync(new Uri("/api/audit/entries?" + query, UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync(Ct)).Code.ShouldBe("audit.invalid");
    }

    [Fact]
    public async Task Detail_of_a_sign_in_adds_ip_and_user_agent_and_a_bad_id_is_404()
    {
        var (_, list) = await GetAsync($"/api/audit/entries?{Wide}&kind=LoginFailed&q=bad%20password");
        var id = list.GetProperty("items")[0].GetProperty("id").GetString()!;

        var (body, detail) = await GetAsync("/api/audit/entries/" + id);
        var (approvalBody, approval) = await GetAsync("/api/audit/entries/" + (await GetAsync($"/api/audit/entries?{Wide}&source=approval")).Json.GetProperty("items")[0].GetProperty("id").GetString());

        detail.GetProperty("fields").EnumerateArray().Select(f => f.GetProperty("label").GetString()).ShouldBe(["IP address", "User agent"]);
        body.ShouldContain("203.0.113.9");
        body.ShouldNotContain("stranger.invalid");
        approval.GetProperty("fields").EnumerateArray().Select(f => f.GetProperty("label").GetString()).ShouldContain("Cutoff");
        approvalBody.ShouldNotContain("eyJhbGci");
        foreach (var bad in new[] { "nope:1", "approval:abc", "approval:999999", "auth:not-a-uuid", "auth:" + Guid.NewGuid(), "x" })
        {
            using var response = await Client.GetAsync(new Uri("/api/audit/entries/" + bad, UriKind.Relative), Ct);

            response.StatusCode.ShouldBe(HttpStatusCode.NotFound, bad);
            (await response.ReadProblemAsync(Ct)).Code.ShouldBe("audit.not_found");
        }
    }

    [Fact]
    public async Task The_summary_counts_the_window_and_the_kinds_list_has_every_source()
    {
        var (_, summary) = await GetAsync($"/api/audit/summary?{Wide}");
        var (_, kinds) = await GetAsync("/api/audit/kinds");

        summary.GetProperty("total").GetInt32().ShouldBe(summary.GetProperty("bySource").EnumerateArray().Sum(c => c.GetProperty("count").GetInt32()));
        summary.GetProperty("byOutcome").EnumerateArray().Select(c => c.GetProperty("key").GetString()).ShouldContain("failed");
        kinds.GetProperty("kinds").EnumerateArray().Select(k => k.GetProperty("source").GetString()).Distinct().Order().ShouldBe(SourceCodes.Order());
    }

    [Fact]
    public async Task Reading_the_audit_writes_nothing_to_it()
    {
        var before = await ScalarAsync<long>("SELECT (SELECT count(*) FROM auth.audit_events) + (SELECT count(*) FROM accounts.account_events) + (SELECT count(*) FROM workflow.approvals) + (SELECT count(*) FROM trading.plan_events) + (SELECT count(*) FROM workflow.autonomy_settings) + (SELECT count(*) FROM research.learning_changelog)");

        var statements = await Client.StatementsDuringAsync(
            async () =>
            {
                (await Client.GetAsync(new Uri($"/api/audit/entries?{Wide}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
                (await Client.GetAsync(new Uri($"/api/audit/summary?{Wide}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
            },
            readsOnly: false);

        statements.ShouldNotBeEmpty();
        statements.Where(s => s.Contains("audit_events", StringComparison.Ordinal) || s.Contains("account_events", StringComparison.Ordinal) || s.Contains("approvals", StringComparison.Ordinal) || s.Contains("plan_events", StringComparison.Ordinal) || s.Contains("autonomy_settings", StringComparison.Ordinal) || s.Contains("learning_changelog", StringComparison.Ordinal))
            .ShouldAllBe(s => s.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase));
        (await ScalarAsync<long>("SELECT (SELECT count(*) FROM auth.audit_events) + (SELECT count(*) FROM accounts.account_events) + (SELECT count(*) FROM workflow.approvals) + (SELECT count(*) FROM trading.plan_events) + (SELECT count(*) FROM workflow.autonomy_settings) + (SELECT count(*) FROM research.learning_changelog)")).ShouldBe(before);
    }

    [Fact]
    public async Task At_volume_the_first_page_and_the_summary_stay_fast()
    {
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "INSERT INTO accounts.account_events (id, at, account_id, event, detail, actor) SELECT gen_random_uuid(), @at - (g * interval '20 seconds'), NULL, 'DataUpdated', 'bulk row', 'system:bulk' FROM generate_series(1, 100000) g", ("at", Now));
            await ExecuteAsync(connection, "INSERT INTO auth.audit_events (id, at, event, email, ip, user_agent, detail) SELECT gen_random_uuid(), @at - (g * interval '2 minutes'), 'LoginSucceeded', 'owner@example.invalid', NULL, NULL, NULL FROM generate_series(1, 20000) g", ("at", Now));
            await ExecuteAsync(connection, "INSERT INTO research.learning_changelog (kind, subject_kind, subject, from_state, to_state, reason, actor, evidence_ids, created_at) SELECT 'transition', 'cell', 'bulk|' || g, 'research', 'candidate', 'bulk', 'cli:bulk', '{}', @at - (g * interval '2 minutes') FROM generate_series(1, 20000) g", ("at", Now));
        }

        var list = new List<double>();
        var summary = new List<double>();
        for (var i = 0; i < 20; i++)
        {
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            using var page = await Client.GetAsync(new Uri("/api/audit/entries", UriKind.Relative), Ct);
            list.Add(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            page.StatusCode.ShouldBe(HttpStatusCode.OK);
            started = System.Diagnostics.Stopwatch.GetTimestamp();
            using var counts = await Client.GetAsync(new Uri("/api/audit/summary", UriKind.Relative), Ct);
            summary.Add(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            counts.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        await using var cleanup = await OpenAsync();
        await ExecuteAsync(cleanup, "DELETE FROM accounts.account_events WHERE actor = 'system:bulk'");
        await ExecuteAsync(cleanup, "DELETE FROM research.learning_changelog WHERE actor = 'cli:bulk'");
        await ExecuteAsync(cleanup, "DELETE FROM auth.audit_events WHERE at < @at AND event = 'LoginSucceeded' AND ip IS NULL", ("at", Now.AddMinutes(-1)));
        list.Sort();
        summary.Sort();
        TestContext.Current.SendDiagnosticMessage($"audit volume: list p95 {list[18]:F0} ms, summary p95 {summary[18]:F0} ms");
        list[18].ShouldBeLessThan(300);
        summary[18].ShouldBeLessThan(500);

    }

    [Fact]
    public void Every_audit_endpoint_is_a_get()
    {
        var routes = Factory.Services.GetServices<Microsoft.AspNetCore.Routing.EndpointDataSource>().SelectMany(d => d.Endpoints).OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/audit", StringComparison.Ordinal) == true).ToList();

        routes.ShouldNotBeEmpty();
        routes.SelectMany(e => e.Metadata.OfType<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()).SelectMany(m => m.HttpMethods).Distinct().ShouldBe(["GET"]);
    }
}
