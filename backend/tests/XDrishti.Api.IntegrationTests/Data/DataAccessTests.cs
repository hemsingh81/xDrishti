using System.Net;
using System.Text;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Data;

/// <summary>Every data endpoint needs a session; starting a job also needs the CSRF token but no step-up (it only reads the mounted folder and a token that is already stored).</summary>
[Collection(ApiTestSuite.Name)]
public sealed class DataAccessTests(ApiFactory factory)
{
    private static readonly string Id = Guid.NewGuid().ToString();

    private static readonly string[] AggregateOnly = ["aggregate"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> AllEndpoints => new()
    {
        { "GET", "/api/data/overview" },
        { "GET", "/api/data/instruments" },
        { "GET", "/api/data/instruments?q=abb&status=behind&sort=-bars&page=2&pageSize=10" },
        { "GET", $"/api/data/instruments/{Id}/bars" },
        { "GET", $"/api/data/instruments/{Id}/bars?timeframe=1m&take=10" },
        { "POST", "/api/data/refresh" },
        { "GET", "/api/data/jobs" },
        { "GET", $"/api/data/jobs/{Id}" },
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
        factory.DataJobs.PendingCount.ShouldBe(0);
    }

    [Fact]
    public async Task Starting_a_job_without_the_CSRF_token_is_refused_even_with_a_session()
    {
        var signedIn = await factory.SignInAsync(Ct);
        var client = factory.CreateAnonymousClient();
        client.DefaultRequestHeaders.Add("Cookie", signedIn.CookieHeader);

        using var response = await client.PostAsync("/api/data/refresh", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        factory.DataJobs.PendingCount.ShouldBe(0);
    }

    [Theory]
    [InlineData("/api/data/overview")]
    [InlineData("/api/data/instruments")]
    [InlineData("/api/data/jobs")]
    public async Task With_a_session_the_reads_work_without_step_up(string path)
    {
        var signedIn = await factory.SignInAsync(Ct);

        using var response = await signedIn.Client.GetAsync(path, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task With_a_session_and_the_CSRF_token_a_job_starts_without_step_up()
    {
        await factory.ResetInstrumentsAsync();
        var signedIn = await factory.SignInAsync(Ct);

        using var response = await signedIn.Client.PostJsonAsync("/api/data/refresh", new { steps = AggregateOnly }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        factory.DataJobs.Clear();
        await factory.ResetInstrumentsAsync();
    }
}
