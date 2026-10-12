using System.Net;
using System.Text;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Market;

/// <summary>Every basket endpoint needs a session; changes also need the CSRF token; none needs a step-up (baskets only group instruments).</summary>
[Collection(ApiTestSuite.Name)]
public sealed class BasketsAccessTests(ApiFactory factory)
{
    private static readonly string Id = Guid.NewGuid().ToString();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> Reads => new()
    {
        { "GET", "/api/baskets" },
        { "GET", "/api/baskets?includeArchived=true" },
        { "GET", $"/api/baskets/{Id}/members" },
        { "GET", $"/api/baskets/{Id}/members?asOf=2026-10-11" },
        { "GET", $"/api/baskets/{Id}/history" },
        { "GET", $"/api/instruments/{Id}/baskets" },
    };

    public static TheoryData<string, string> Changes => new()
    {
        { "POST", "/api/baskets" },
        { "PUT", $"/api/baskets/{Id}" },
        { "DELETE", $"/api/baskets/{Id}" },
        { "PUT", "/api/baskets/order" },
        { "POST", $"/api/baskets/{Id}/members" },
        { "DELETE", $"/api/baskets/{Id}/members" },
        { "PUT", $"/api/baskets/{Id}/members/order" },
        { "POST", "/api/baskets/transfer" },
    };

    private static HttpRequestMessage Request(string method, string path) => new(new HttpMethod(method), new Uri(path, UriKind.Relative))
    {
        Content = method == "GET" ? null : new StringContent("{}", Encoding.UTF8, "application/json"),
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
    public async Task A_change_without_the_CSRF_token_is_refused_with_403_and_with_it_needs_no_step_up(string method, string path)
    {
        var signedIn = await factory.SignInAsync(Ct);
        var withoutToken = factory.CreateAnonymousClient();
        withoutToken.DefaultRequestHeaders.Add("Cookie", signedIn.CookieHeader);

        using var refused = await withoutToken.SendAsync(Request(method, path), Ct);
        using var allowed = await signedIn.Client.SendAsync(Request(method, path), Ct);

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        allowed.StatusCode.ShouldNotBe(HttpStatusCode.Forbidden, "a step-up would answer 403 auth.step_up_required");
        allowed.StatusCode.ShouldNotBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(Reads))]
    public async Task With_a_session_the_reads_need_no_step_up(string method, string path)
    {
        var signedIn = await factory.SignInAsync(Ct);

        using var response = await signedIn.Client.SendAsync(Request(method, path), Ct);

        response.StatusCode.ShouldBeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            (await response.ReadProblemAsync(Ct)).Code.ShouldNotBeNull().ShouldEndWith(".not_found");
        }
    }
}
