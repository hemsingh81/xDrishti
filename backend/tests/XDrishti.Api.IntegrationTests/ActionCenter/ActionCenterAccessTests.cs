using System.Net;
using System.Text;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.ActionCenter;

/// <summary>Every Action-center and alert endpoint needs a session; every change also needs the CSRF token; none needs a step-up (P2-09).</summary>
[Collection(ApiTestSuite.Name)]
public sealed class ActionCenterAccessTests(ApiFactory factory) : IAsyncLifetime
{
    private const string AnyGuid = "00000000-0000-0000-0000-000000000001";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await factory.ResetAccountsAsync();
        await factory.ResetInstrumentsAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public static TheoryData<string, string> Reads => new()
    {
        { "GET", "/api/portfolio/actions" },
        { "GET", "/api/portfolio/actions?status=all" },
        { "GET", "/api/portfolio/action-thresholds" },
        { "GET", "/api/portfolio/alerts" },
        { "GET", "/api/portfolio/notices" },
    };

    public static TheoryData<string, string> Changes => new()
    {
        { "POST", $"/api/portfolio/actions/below_cost:{AnyGuid}/snooze" },
        { "POST", $"/api/portfolio/actions/below_cost:{AnyGuid}/done" },
        { "POST", $"/api/portfolio/actions/below_cost:{AnyGuid}/reopen" },
        { "PUT", "/api/portfolio/action-thresholds" },
        { "POST", "/api/portfolio/action-thresholds/reset" },
        { "POST", "/api/portfolio/alerts" },
        { "POST", $"/api/portfolio/alerts/{AnyGuid}/cancel" },
    };

    private static HttpRequestMessage Request(string method, string path) => new(new HttpMethod(method), new Uri(path, UriKind.Relative))
    {
        Content = method is "POST" or "PUT" ? new StringContent("{}", Encoding.UTF8, "application/json") : null,
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
    }

    [Theory]
    [MemberData(nameof(Reads))]
    public async Task With_a_session_the_reads_work_without_step_up(string method, string path)
    {
        var signedIn = await factory.SignInAsync(Ct);

        using var response = await signedIn.Client.SendAsync(Request(method, path), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_changes_need_no_step_up()
    {
        var signedIn = await factory.SignInAsync(Ct);

        using var snooze = await signedIn.Client.SendAsync(Request("POST", $"/api/portfolio/actions/below_cost:{AnyGuid}/snooze"), Ct);
        using var reset = await signedIn.Client.SendAsync(Request("POST", "/api/portfolio/action-thresholds/reset"), Ct);
        using var save = await signedIn.Client.SendAsync(Request("PUT", "/api/portfolio/action-thresholds"), Ct);
        using var alert = await signedIn.Client.SendAsync(Request("POST", "/api/portfolio/alerts"), Ct);
        using var cancel = await signedIn.Client.SendAsync(Request("POST", $"/api/portfolio/alerts/{AnyGuid}/cancel"), Ct);

        snooze.StatusCode.ShouldBe(HttpStatusCode.NotFound); // a business answer for a stale id, not a step-up refusal
        reset.StatusCode.ShouldBe(HttpStatusCode.OK);
        save.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        alert.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        cancel.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
