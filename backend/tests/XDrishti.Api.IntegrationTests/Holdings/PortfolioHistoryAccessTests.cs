using System.Net;
using System.Text;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Holdings;

/// <summary>Every portfolio history endpoint needs a session; changing the benchmark and the track actions also need the CSRF token; none needs a step-up (P2-07).</summary>
[Collection(ApiTestSuite.Name)]
public sealed class PortfolioHistoryAccessTests(ApiFactory factory) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await factory.ResetInstrumentsAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public static TheoryData<string, string> AllEndpoints => new()
    {
        { "GET", "/api/portfolio/history" },
        { "GET", "/api/portfolio/history?accountId=00000000-0000-0000-0000-000000000001&resolution=week" },
        { "GET", "/api/portfolio/benchmark" },
        { "PUT", "/api/portfolio/benchmark" },
        { "POST", "/api/portfolio/history/track-holdings" },
        { "POST", "/api/portfolio/history/track-benchmark" },
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
    [InlineData("PUT", "/api/portfolio/benchmark")]
    [InlineData("POST", "/api/portfolio/history/track-holdings")]
    [InlineData("POST", "/api/portfolio/history/track-benchmark")]
    public async Task A_write_without_the_CSRF_token_is_refused_even_with_a_session(string method, string path)
    {
        var signedIn = await factory.SignInAsync(Ct);
        var client = factory.CreateAnonymousClient();
        client.DefaultRequestHeaders.Add("Cookie", signedIn.CookieHeader);

        using var response = await client.SendAsync(Request(method, path), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("/api/portfolio/history")]
    [InlineData("/api/portfolio/benchmark")]
    public async Task With_a_session_the_reads_work_without_step_up(string path)
    {
        var signedIn = await factory.SignInAsync(Ct);

        using var response = await signedIn.Client.GetAsync(path, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_writes_need_no_step_up()
    {
        var signedIn = await factory.SignInAsync(Ct);

        using var track = await signedIn.Client.PostAsync("/api/portfolio/history/track-holdings", Ct);
        using var benchmark = await signedIn.Client.PostAsync("/api/portfolio/history/track-benchmark", Ct);
        using var choose = await signedIn.Client.PutJsonAsync("/api/portfolio/benchmark", new { }, Ct);

        track.StatusCode.ShouldBe(HttpStatusCode.OK);
        benchmark.StatusCode.ShouldBe(HttpStatusCode.NotFound); // the master list has no index: a business answer, not a step-up refusal
        choose.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
