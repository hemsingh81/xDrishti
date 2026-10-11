using System.Net;
using System.Text;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Accounts;

/// <summary>Every accounts endpoint needs a session; the sensitive ones also need a recent password re-entry.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class AccountsAccessTests(ApiFactory factory)
{
    private static readonly string Id = Guid.NewGuid().ToString();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> SensitiveEndpoints => new()
    {
        { "POST", "/api/accounts" },
        { "PUT", $"/api/accounts/{Id}/connection" },
        { "PUT", $"/api/accounts/{Id}/secrets" },
        { "PUT", $"/api/accounts/{Id}/roles" },
        { "POST", $"/api/accounts/{Id}/make-primary-data" },
        { "PUT", $"/api/accounts/{Id}/trading" },
        { "DELETE", $"/api/accounts/{Id}" },
        { "POST", $"/api/accounts/{Id}/connect" },
        { "DELETE", $"/api/accounts/{Id}/token" },
        { "POST", "/api/risk-profiles" },
        { "PUT", $"/api/risk-profiles/{Id}" },
        { "DELETE", $"/api/risk-profiles/{Id}" },
    };

    public static TheoryData<string, string> AllEndpoints => new()
    {
        { "GET", "/api/accounts" },
        { "GET", $"/api/accounts/{Id}" },
        { "PUT", $"/api/accounts/{Id}/portfolio" },
        { "PUT", $"/api/accounts/{Id}/data" },
        { "GET", $"/api/accounts/{Id}/activity" },
        { "GET", "/api/brokers/dhan" },
        { "GET", "/api/risk-profiles" },
        { "GET", "/api/portfolio-buckets" },
        { "POST", "/api/portfolio-buckets" },
        { "POST", "/api/auth/step-up" },
    };

    private static HttpRequestMessage Request(string method, string path) => new(new HttpMethod(method), new Uri(path, UriKind.Relative))
    {
        Content = method is "POST" or "PUT" ? new StringContent("{}", Encoding.UTF8, "application/json") : null,
    };

    [Theory]
    [MemberData(nameof(SensitiveEndpoints))]
    [MemberData(nameof(AllEndpoints))]
    public async Task Without_a_session_every_endpoint_answers_401(string method, string path)
    {
        using var response = await factory.CreateAnonymousClient().SendAsync(Request(method, path), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(SensitiveEndpoints))]
    public async Task Without_step_up_a_sensitive_endpoint_answers_403_step_up_required(string method, string path)
    {
        var signedIn = await factory.SignInAsync(Ct);

        using var response = await signedIn.Client.SendAsync(Request(method, path), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.ReadProblemAsync(Ct)).Code.ShouldBe("auth.step_up_required");
    }

    [Theory]
    [InlineData("PUT", "/api/accounts/{0}/token")]
    [InlineData("POST", "/api/accounts/{0}/token/renew")]
    [InlineData("POST", "/api/accounts/{0}/token/test")]
    [InlineData("GET", "/api/brokers")]
    [InlineData("GET", "/api/brokers/upstox/callback?code=x&state=y")]
    public async Task The_removed_token_and_broker_endpoints_are_gone(string method, string template)
    {
        var signedIn = await factory.SignInElevatedAsync(Ct);

        using var response = await signedIn.Client.SendAsync(Request(method, string.Format(System.Globalization.CultureInfo.InvariantCulture, template, Id)), Ct);

        response.StatusCode.ShouldBeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task Without_a_session_the_Dhan_callback_redirects_back_to_the_app_instead_of_answering_401()
    {
        using var response = await factory.CreateAnonymousClient().GetAsync("/api/brokers/dhan/callback?tokenId=x", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        response.Headers.Location!.ToString().ShouldBe("http://localhost:8080/accounts?connectError=auth.unauthenticated");
    }

    [Theory]
    [InlineData("/api/accounts/{0}/credentials")]
    [InlineData("/api/accounts/credentials/some_ref")]
    public async Task The_removed_credential_reference_endpoints_are_gone(string template)
    {
        var signedIn = await factory.SignInAsync(Ct);

        using var response = await signedIn.Client.GetAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, template, Id), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("/api/accounts/not-a-guid")]
    [InlineData("/api/accounts/not-a-guid/activity")]
    public async Task A_malformed_id_is_not_found(string path)
    {
        var signedIn = await factory.SignInAsync(Ct);

        (await signedIn.Client.GetAsync(path, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
