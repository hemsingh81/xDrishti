using System.Net;
using System.Text;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.BrokerHistory;

/// <summary>The broker history endpoints need a session; "Sync now" also needs the CSRF token; none needs a step-up (P2-13, ADR 0025).</summary>
[Collection(ApiTestSuite.Name)]
public sealed class BrokerHistoryAccessTests(ApiFactory factory) : IAsyncLifetime
{
    private const string AnyGuid = "00000000-0000-0000-0000-000000000001";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await factory.ResetAccountsAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public static TheoryData<string, string> Reads => new()
    {
        { "GET", $"/api/accounts/{AnyGuid}/sync/broker-history/status" },
        { "GET", $"/api/accounts/{AnyGuid}/ledger" },
    };

    public static TheoryData<string, string> Changes => new()
    {
        { "POST", $"/api/accounts/{AnyGuid}/sync/broker-history" },
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
    public async Task With_a_session_and_the_token_sync_now_needs_no_step_up()
    {
        await factory.ResetClockAsync();
        var signedIn = await factory.SignInAsync(Ct);

        using var response = await signedIn.Client.PostJsonAsync($"/api/accounts/{AnyGuid}/sync/broker-history", new { from = "2026-10-01" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound); // the account does not exist; it is not a step-up refusal
        (await response.ReadProblemAsync(Ct)).Code.ShouldBe("accounts.not_found");
    }
}
