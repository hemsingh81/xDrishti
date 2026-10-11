using System.Net;
using System.Text;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Market;

/// <summary>Every instrument endpoint needs a session; the refresh only reads public files, so it needs no step-up.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class MarketAccessTests(ApiFactory factory)
{
    private static readonly string Id = Guid.NewGuid().ToString();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> AllEndpoints => new()
    {
        { "GET", "/api/instruments" },
        { "GET", "/api/instruments?q=rel&pageSize=10" },
        { "GET", "/api/instruments/facets" },
        { "GET", $"/api/instruments/{Id}" },
        { "POST", "/api/instruments/refresh" },
        { "GET", "/api/instruments/refresh/status" },
        { "GET", "/api/instruments/refresh/history" },
    };

    private static HttpRequestMessage Request(string method, string path) => new(new HttpMethod(method), new Uri(path, UriKind.Relative))
    {
        Content = method == "POST" ? new StringContent("{}", Encoding.UTF8, "application/json") : null,
    };

    [Theory]
    [MemberData(nameof(AllEndpoints))]
    public async Task Without_a_session_every_endpoint_answers_401(string method, string path)
    {
        using var response = await factory.CreateAnonymousClient().SendAsync(Request(method, path), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.ReadProblemAsync(Ct)).Code.ShouldBe("auth.unauthenticated");
    }

    [Fact]
    public async Task A_refresh_without_the_CSRF_token_is_refused_even_with_a_session()
    {
        var signedIn = await factory.SignInAsync(Ct);
        var client = factory.CreateAnonymousClient();
        client.DefaultRequestHeaders.Add("Cookie", signedIn.CookieHeader);

        using var response = await client.PostAsync("/api/instruments/refresh", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        factory.RefreshJobs.PendingCount.ShouldBe(0);
    }

    [Theory]
    [InlineData("/api/instruments")]
    [InlineData("/api/instruments/facets")]
    [InlineData("/api/instruments/refresh/status")]
    [InlineData("/api/instruments/refresh/history")]
    public async Task With_a_session_the_reads_work_without_step_up(string path)
    {
        var signedIn = await factory.SignInAsync(Ct);

        using var response = await signedIn.Client.GetAsync(path, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_contract_lists_the_instrument_operations()
    {
        var signedIn = await factory.SignInAsync(Ct);

        var contract = await signedIn.Client.GetAsync("/api/openapi/v1.json", Ct);
        var json = await contract.Content.ReadAsStringAsync(Ct);

        foreach (var operation in new[] { "SearchInstruments", "GetInstrumentFacets", "GetInstrument", "StartInstrumentRefresh", "GetInstrumentRefreshStatus", "ListInstrumentRefreshes" })
        {
            json.ShouldContain($"\"operationId\": \"{operation}\"");
        }
    }
}
