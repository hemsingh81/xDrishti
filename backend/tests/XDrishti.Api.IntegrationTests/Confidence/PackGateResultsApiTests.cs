using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Lab;

namespace XDrishti.Api.IntegrationTests.Confidence;

/// <summary>
/// CF-11 row 15 (SC-S4a, ADR 0226) over HTTP and a real database: the owner records a verify-pack --gate result with a fresh password; the record is insert-only; the scorecard's row 15 reads PASS over 20 trades as pass, any other status as fail, and no record as unavailable.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class PackGateResultsApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly Uri Record = new("/api/confidence/pack-gate-results", UriKind.Relative);

    private static Uri Card(string strategy, int version) => new($"/api/confidence/scorecards/{strategy}/versions/{version}", UriKind.Relative);

    private async Task<(HttpStatusCode Status, JsonElement Body)> SendAsync(HttpMethod method, Uri uri, object? body = null, HttpClient? client = null)
    {
        using var request = new HttpRequestMessage(method, uri) { Content = body is null ? null : JsonContent.Create(body) };
        using var response = await (client ?? Client).SendAsync(request, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private Task<(HttpStatusCode Status, JsonElement Body)> RecordAsync(string strategy, int version, string status, int trades, string? password, string? reason = "gate printed it") =>
        SendAsync(HttpMethod.Post, Record, new { strategyId = strategy, version, packId = "pack-abc", status, tradeCount = trades, reason, password, actor = "owner:evil@example.test" });

    private async Task<string> CreateStrategyAsync()
    {
        var id = LabTexts.NewId();
        using var response = await Client.PostAsJsonAsync(new Uri("/api/lab/strategies", UriKind.Relative), new { text = LabTexts.Valid(id) }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return id;
    }

    private async Task AddSecondVersionAsync(string id)
    {
        using var response = await Client.PostAsJsonAsync(new Uri("/api/lab/strategies", UriKind.Relative), new { text = LabTexts.Changed(id) }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    /// <summary>The P3-09 term as the row's value text says it (<c>20-trade check pass|fail|unavailable</c>); the whole row also needs the phase check and the edge series, which a fresh strategy does not have.</summary>
    private async Task<string> Row15Async(string strategy)
    {
        var card = await SendAsync(HttpMethod.Get, Card(strategy, 1));
        card.Status.ShouldBe(HttpStatusCode.OK);
        return card.Body.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("number").GetInt32() == 15).GetProperty("value").GetString()!;
    }

    [Fact]
    public async Task Row_15_follows_the_newest_record_and_is_unavailable_without_one()
    {
        var id = await CreateStrategyAsync();
        (await Row15Async(id)).ShouldStartWith("20-trade check unavailable");

        (await RecordAsync(id, 1, "PASS_WITH_DATA_FINDINGS", 40, Factory.OwnerPassword)).Status.ShouldBe(HttpStatusCode.OK);
        (await Row15Async(id)).ShouldStartWith("20-trade check fail");

        var pass = await RecordAsync(id, 1, "PASS", 20, Factory.OwnerPassword);
        pass.Status.ShouldBe(HttpStatusCode.OK);
        pass.Body.GetProperty("countsAsPass").GetBoolean().ShouldBeTrue();
        (await Row15Async(id)).ShouldStartWith("20-trade check pass");

        (await RecordAsync(id, 1, "BLOCKED_DEFECT", 20, Factory.OwnerPassword)).Status.ShouldBe(HttpStatusCode.OK);
        (await Row15Async(id)).ShouldStartWith("20-trade check fail");
        (await ScalarAsync<long>($"SELECT count(*) FROM readiness.pack_gate_results WHERE strategy_id = '{id}'")).ShouldBe(3, "every record is a row");
    }

    [Fact]
    public async Task A_record_needs_a_fresh_password_and_writes_nothing_without_it()
    {
        var id = await CreateStrategyAsync();

        var missing = await RecordAsync(id, 1, "PASS", 20, null);
        var wrong = await RecordAsync(id, 1, "PASS", 20, "not-the-password");

        missing.Status.ShouldBe(HttpStatusCode.Forbidden);
        missing.Body.GetProperty("code").GetString().ShouldBe("auth.step_up_required");
        wrong.Status.ShouldBe(HttpStatusCode.Unauthorized);
        (await ScalarAsync<long>($"SELECT count(*) FROM readiness.pack_gate_results WHERE strategy_id = '{id}'")).ShouldBe(0);
    }

    [Fact]
    public async Task The_actor_is_the_signed_in_owner_and_never_one_named_in_the_body()
    {
        var id = await CreateStrategyAsync();
        await RecordAsync(id, 1, "PASS", 20, Factory.OwnerPassword);

        (await ScalarAsync<string>($"SELECT actor FROM readiness.pack_gate_results WHERE strategy_id = '{id}'")).ShouldBe("owner:" + ApiFactory.OwnerEmail);
    }

    [Fact]
    public async Task Bad_records_are_refused_with_their_codes()
    {
        var id = await CreateStrategyAsync();

        var status = await RecordAsync(id, 1, "pass", 20, Factory.OwnerPassword);
        var reason = await RecordAsync(id, 1, "PASS", 20, Factory.OwnerPassword, " ");
        var version = await RecordAsync(id, 9, "PASS", 20, Factory.OwnerPassword);
        var strategy = await RecordAsync("NOPE_NOT_STORED", 1, "PASS", 20, Factory.OwnerPassword);

        status.Status.ShouldBe(HttpStatusCode.BadRequest);
        status.Body.GetProperty("code").GetString().ShouldBe("confidence.readiness_invalid");
        reason.Status.ShouldBe(HttpStatusCode.BadRequest);
        reason.Body.GetProperty("code").GetString().ShouldBe("confidence.reason_invalid");
        version.Status.ShouldBe(HttpStatusCode.NotFound);
        strategy.Status.ShouldBe(HttpStatusCode.NotFound);
        (await ScalarAsync<long>($"SELECT count(*) FROM readiness.pack_gate_results WHERE strategy_id = '{id}'")).ShouldBe(0);
    }

    [Fact]
    public async Task A_record_without_the_CSRF_token_is_refused_even_with_the_session_and_password()
    {
        var id = await CreateStrategyAsync();
        var client = Factory.CreateAnonymousClient();
        client.DefaultRequestHeaders.Add("Cookie", Owner.CookieHeader);

        var result = await SendAsync(HttpMethod.Post, Record, new { strategyId = id, version = 1, packId = "pack-abc", status = "PASS", tradeCount = 20, reason = "gate printed it", password = Factory.OwnerPassword }, client);

        result.Status.ShouldBe(HttpStatusCode.Forbidden);
        result.Body.GetProperty("code").GetString().ShouldBe("auth.csrf_invalid");
        (await ScalarAsync<long>($"SELECT count(*) FROM readiness.pack_gate_results WHERE strategy_id = '{id}'")).ShouldBe(0);
    }

    [Fact]
    public async Task A_record_for_a_version_that_belongs_to_another_strategy_is_not_found_and_writes_nothing()
    {
        var a = await CreateStrategyAsync();
        var b = await CreateStrategyAsync();
        await AddSecondVersionAsync(b);
        (await SendAsync(HttpMethod.Get, Card(b, 2))).Status.ShouldBe(HttpStatusCode.OK);

        var result = await RecordAsync(a, 2, "PASS", 20, Factory.OwnerPassword);

        result.Status.ShouldBe(HttpStatusCode.NotFound);
        (await ScalarAsync<long>($"SELECT count(*) FROM readiness.pack_gate_results WHERE strategy_id = '{a}'")).ShouldBe(0);
    }

    [Fact]
    public async Task A_record_without_a_session_is_refused()
    {
        var anonymous = Factory.CreateAnonymousClient();

        var result = await SendAsync(HttpMethod.Post, Record, new { strategyId = "x", version = 1, packId = "p", status = "PASS", tradeCount = 20, reason = "x", password = Factory.OwnerPassword }, anonymous);

        result.Status.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
