using System.Net;
using System.Text;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.DividendImport;

/// <summary>Every dividend import endpoint needs a session; every change also needs the CSRF token; none needs a step-up (P2-15).</summary>
[Collection(ApiTestSuite.Name)]
public sealed class DividendImportAccessTests(ApiFactory factory) : IAsyncLifetime
{
    private const string AnyGuid = "00000000-0000-0000-0000-000000000001";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await factory.ResetAccountsAsync();

    public async ValueTask DisposeAsync() => await factory.ResetAccountsAsync();

    public static TheoryData<string, string> Reads => new()
    {
        { "GET", "/api/portfolio/dividend-batches" },
    };

    public static TheoryData<string, string> Changes => new()
    {
        { "POST", "/api/portfolio/dividends/preview" },
        { "POST", "/api/portfolio/dividends/import" },
        { "POST", $"/api/portfolio/dividend-batches/{AnyGuid}/undo" },
    };

    private static HttpRequestMessage Request(string method, string path) => new(new HttpMethod(method), new Uri(path, UriKind.Relative))
    {
        Content = method == "POST" ? new StringContent("{}", Encoding.UTF8, "application/json") : null,
    };

    [Theory]
    [MemberData(nameof(Reads))]
    [MemberData(nameof(Changes))]
    public async Task Without_a_session_every_endpoint_answers_401(string method, string path)
    {
        using var response = await factory.CreateAnonymousClient().SendAsync(Request(method, path), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.ReadProblemAsync(Ct)).Code.ShouldBe("auth.unauthenticated");
    }

    [Theory]
    [MemberData(nameof(Changes))]
    public async Task A_change_without_the_CSRF_token_is_refused_even_with_a_session(string method, string path)
    {
        var signedIn = await factory.SignInAsync(Ct);
        var client = factory.CreateAnonymousClient();
        client.DefaultRequestHeaders.Add("Cookie", signedIn.CookieHeader);

        using var response = await client.SendAsync(Request(method, path), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.ReadProblemAsync(Ct)).Code.ShouldBe("auth.csrf_invalid");
    }

    [Fact]
    public async Task With_a_session_and_the_token_an_undo_needs_no_step_up()
    {
        await factory.ResetClockAsync();
        var signedIn = await factory.SignInAsync(Ct);

        using var response = await signedIn.Client.PostAsync($"/api/portfolio/dividend-batches/{Guid.NewGuid()}/undo", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound); // the import does not exist; it is not a step-up refusal
        (await response.ReadProblemAsync(Ct)).Code.ShouldBe("portfolio.not_found");
    }
}
