using System.Net;
using System.Text;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Holdings;

/// <summary>Every holdings endpoint needs a session; reading and syncing public portfolio data needs no step-up.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class HoldingsAccessTests(ApiFactory factory) : IAsyncLifetime
{
    private static readonly string Id = Guid.NewGuid().ToString();

    private static readonly string LotId = Guid.NewGuid().ToString();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await factory.ResetAccountsAsync();

    public async ValueTask DisposeAsync() => await factory.ResetAccountsAsync();

    public static TheoryData<string, string> AllEndpoints => new()
    {
        { "GET", "/api/holdings" },
        { "GET", "/api/holdings?accountId=" + "00000000-0000-0000-0000-000000000001&status=all&pageSize=10" },
        { "GET", "/api/holdings?groupBy=sector&groupKey=Banks" },
        { "GET", "/api/holdings/summary" },
        { "GET", "/api/holdings/groups?by=account" },
        { "GET", "/api/holdings/groups?by=sector&stackBy=account&sort=-value" },
        { "GET", $"/api/holdings/{Id}/lots" },
        { "PUT", $"/api/holdings/{Id}/lots/{LotId}" },
        { "POST", $"/api/accounts/{Id}/holdings/preview" },
        { "PUT", $"/api/accounts/{Id}/portfolio" },
        { "POST", $"/api/accounts/{Id}/holdings/sync" },
        { "GET", $"/api/accounts/{Id}/holdings/sync-status" },
    };

    private static HttpRequestMessage Request(string method, string path) => new(new HttpMethod(method), new Uri(path, UriKind.Relative))
    {
        Content = method is "POST" or "PUT" ? new StringContent("{}", Encoding.UTF8, "application/json") : null,
    };

    [Theory]
    [MemberData(nameof(AllEndpoints))]
    public async Task Without_a_session_every_endpoint_answers_401(string method, string path)
    {
        using var response = await factory.CreateAnonymousClient().SendAsync(Request(method, path), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.ReadProblemAsync(Ct)).Code.ShouldBe("auth.unauthenticated");
    }

    [Theory]
    [InlineData("POST", "preview")]
    [InlineData("POST", "sync")]
    public async Task A_write_without_the_CSRF_token_is_refused_even_with_a_session(string method, string action)
    {
        var signedIn = await factory.SignInAsync(Ct);
        var client = factory.CreateAnonymousClient();
        client.DefaultRequestHeaders.Add("Cookie", signedIn.CookieHeader);

        using var response = await client.SendAsync(Request(method, $"/api/accounts/{Id}/holdings/{action}"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        factory.Brokers.Requests.Where(r => r.Uri.AbsolutePath == "/v2/holdings").ShouldBeEmpty();
    }

    [Theory]
    [InlineData("/api/holdings")]
    [InlineData("/api/holdings/summary")]
    public async Task With_a_session_the_reads_work_without_step_up(string path)
    {
        var signedIn = await factory.SignInAsync(Ct);

        using var response = await signedIn.Client.GetAsync(path, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Preview_and_sync_need_no_step_up_the_refusal_is_about_the_account()
    {
        var signedIn = await factory.SignInAsync(Ct);

        foreach (var action in new[] { "preview", "sync" })
        {
            using var response = await signedIn.Client.SendAsync(Request("POST", $"/api/accounts/{Id}/holdings/{action}"), Ct);

            response.StatusCode.ShouldBe(HttpStatusCode.NotFound, action); // an unknown account, not a missing step-up (which would be 403)
            (await response.ReadProblemAsync(Ct)).Code.ShouldBe("accounts.not_found", action);
        }
    }

    [Fact]
    public async Task The_standalone_import_endpoint_is_gone()
    {
        var signedIn = await factory.SignInElevatedAsync(Ct);

        using var response = await signedIn.Client.SendAsync(Request("POST", $"/api/accounts/{Id}/holdings/import"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_contract_lists_the_holdings_operations_and_the_new_fields()
    {
        var signedIn = await factory.SignInAsync(Ct);

        var contract = await signedIn.Client.GetAsync("/api/openapi/v1.json", Ct);
        var json = await contract.Content.ReadAsStringAsync(Ct);

        foreach (var operation in new[] { "ListHoldings", "GetHoldingsSummary", "GetHoldingLots", "UpdateHoldingLotDate", "PreviewAccountHoldings", "SyncAccountHoldings", "GetAccountHoldingsSyncStatus" })
        {
            json.ShouldContain($"\"operationId\": \"{operation}\"");
        }

        json.ShouldNotContain("ImportAccountHoldings");
        json.ShouldNotContain("refreshIntervalMinutes");
        foreach (var field in new[] { "refreshMode", "refreshDailyTimeIst", "firstImportDate", "hasHoldings", "firstImport", "xirrState", "realisedEstimated", "defaultDateLots", "usesDefaultDate" })
        {
            json.ShouldContain($"\"{field}\"");
        }
    }
}
