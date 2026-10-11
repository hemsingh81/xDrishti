using System.Net;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Auth;

/// <summary>AC 1 and 9: everything is closed by default; only login, session and health are open.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class AccessControlTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/api/system/status")]
    [InlineData("/api/openapi/v1.json")]
    [InlineData("/api/docs")]
    [InlineData("/api/docs/v1")]
    [InlineData("/api/does-not-exist")]
    [InlineData("/api/auth/audit")]
    [InlineData("/api/auth/verify")]
    public async Task Protected_routes_answer_401_problem_without_a_session(string path)
    {
        var response = await factory.CreateAnonymousClient().GetAsync(path, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.ReadProblemAsync(Ct)).Code.ShouldBe("auth.unauthenticated");
        response.Headers.Location.ShouldBeNull(); // never a redirect
    }

    [Fact]
    public async Task Logout_without_a_session_is_401_not_403()
    {
        var response = await factory.CreateAnonymousClient().PostAsync("/api/auth/logout", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Session_endpoint_without_a_cookie_is_401()
    {
        var response = await factory.CreateAnonymousClient().GetAsync("/api/auth/session", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.ReadProblemAsync(Ct)).Code.ShouldBe("auth.unauthenticated");
    }

    [Fact]
    public async Task A_random_cookie_is_not_a_session()
    {
        var client = factory.CreateAnonymousClient();
        client.DefaultRequestHeaders.Add("Cookie", "xd.sid=not-a-real-session-token");

        (await client.GetAsync("/api/system/status", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/api/auth/session", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_endpoints_are_public(string path)
    {
        var response = await factory.CreateAnonymousClient().GetAsync(path, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_docs_page_redirects_to_its_index_only_for_a_signed_in_owner()
    {
        var signedIn = await factory.SignInAsync(Ct);

        var response = await signedIn.Client.GetAsync("/api/docs", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        var index = new Uri(new Uri("http://localhost/api/docs"), response.Headers.Location!);
        index.AbsolutePath.ShouldStartWith("/api/docs");
        (await signedIn.Client.GetAsync(index.AbsolutePath, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("/api/system/status")]
    [InlineData("/api/openapi/v1.json")]
    public async Task A_signed_in_owner_reaches_the_protected_routes(string path)
    {
        var signedIn = await factory.SignInAsync(Ct);

        var response = await signedIn.Client.GetAsync(path, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Verify_is_204_for_a_session_and_401_otherwise()
    {
        var signedIn = await factory.SignInAsync(Ct);

        (await signedIn.Client.GetAsync("/api/auth/verify", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await factory.CreateAnonymousClient().GetAsync("/api/auth/verify", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
