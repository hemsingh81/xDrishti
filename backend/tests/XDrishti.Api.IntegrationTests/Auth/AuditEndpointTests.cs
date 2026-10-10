using System.Net;
using System.Net.Http.Json;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Auth;

namespace XDrishti.Api.IntegrationTests.Auth;

/// <summary>AC 6: sign-in events are recorded and readable by the owner — never passwords or tokens.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class AuditEndpointTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Login_failure_success_and_logout_are_listed_newest_first_with_the_client_address()
    {
        var client = factory.CreateAnonymousClient();
        var secretAttempt = "wrong-" + Guid.NewGuid().ToString("N");
        await client.LoginAsync(ApiFactory.OwnerEmail, secretAttempt, Ct);
        var signedIn = await factory.SignInAsync(Ct);
        await signedIn.Client.PostAsync("/api/auth/logout", Ct);
        var reader = await factory.SignInAsync(Ct);

        var response = await reader.Client.GetAsync("/api/auth/audit?take=50", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(Ct);
        var events = System.Text.Json.JsonSerializer.Deserialize<AuditEventResponse[]>(body, System.Text.Json.JsonSerializerOptions.Web)!;
        events.Length.ShouldBeGreaterThanOrEqualTo(4);
        events.Select(e => e.At).ShouldBe(events.Select(e => e.At).OrderByDescending(a => a));
        events.Take(4).Select(e => e.Event).ShouldBe(["LoginSucceeded", "Logout", "LoginSucceeded", "LoginFailed"]);
        events[3].Email.ShouldBe(ApiFactory.OwnerEmail);
        events[3].Ip.ShouldNotBeNullOrEmpty();

        // never secrets
        body.ShouldNotContain(secretAttempt);
        body.ShouldNotContain(factory.OwnerPassword);
        body.ShouldNotContain(reader.CookieValue);
        body.ShouldNotContain(reader.Session.CsrfToken);
    }

    [Fact]
    public async Task Take_limits_the_list()
    {
        var reader = await factory.SignInAsync(Ct);

        var events = await reader.Client.GetFromJsonAsync<AuditEventResponse[]>(new Uri("/api/auth/audit?take=2", UriKind.Relative), Ct);

        events.ShouldNotBeNull().Length.ShouldBe(2);
    }

    [Fact]
    public async Task The_audit_list_needs_a_session()
    {
        var response = await factory.CreateAnonymousClient().GetAsync("/api/auth/audit", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Provisioning_is_in_the_trail()
    {
        var reader = await factory.SignInAsync(Ct);

        var events = await reader.Client.GetFromJsonAsync<AuditEventResponse[]>(new Uri("/api/auth/audit?take=200", UriKind.Relative), Ct);

        events.ShouldNotBeNull().ShouldContain(e => e.Event == "OwnerProvisioned");
    }
}
