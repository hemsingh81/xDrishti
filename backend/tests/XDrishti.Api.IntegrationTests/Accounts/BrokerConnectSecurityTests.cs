using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Infrastructure.Brokers;

namespace XDrishti.Api.IntegrationTests.Accounts;

/// <summary>
/// Connecting never leaks a token, secret, code or the full client id (responses, redirects, audit, connection rows, logs), and the broker
/// client can only reach the allow-listed Dhan consent endpoints (specs P1-16c, P1-16d).
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class BrokerConnectSecurityTests(ApiFactory factory) : IAsyncLifetime
{
    /// <summary>A client id used by no other test, so its full form can be searched for.</summary>
    private const string LeakClientId = "5550009876";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await factory.ResetAccountsAsync();
        factory.Brokers.Reset();
    }

    public async ValueTask DisposeAsync()
    {
        factory.Brokers.Reset();
        await factory.ResetAccountsAsync();
    }

    [Fact]
    public async Task No_response_redirect_audit_entry_connection_row_or_log_line_contains_a_token_secret_code_or_full_client_id()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        factory.Brokers.DhanClientId = LeakClientId;
        var texts = new List<string>();

        async Task<HttpResponseMessage> Record(Task<HttpResponseMessage> call)
        {
            var response = await call;
            texts.Add($"{response.StatusCode} {response.Headers.Location} {await response.Content.ReadAsStringAsync(Ct)}");
            return response;
        }

        var dhan = await (await Record(client.PostJsonAsync("/api/accounts", AccountsApi.NewAccount(clientId: LeakClientId), Ct))).ReadAsCreatedAsync<Application.Accounts.AccountDetail>(Ct);

        // Sign-in, a replay, a Dhan that echoes what it got in an error, the failed sign-in, disconnect and every read.
        await Record(client.PostAsync($"/api/accounts/{dhan.Id}/connect", Ct));
        await Record(client.DhanCallbackAsync("tid-" + AccountsApi.Marker, Ct));
        await Record(client.DhanCallbackAsync("tid-" + AccountsApi.Marker, Ct));
        factory.Brokers.Override("/app/consumeApp-consent", r => FakeBrokers.Json(HttpStatusCode.BadRequest, $$"""{"errorCode":"{{AccountsApi.Marker}}","errorMessage":"bad {{r.Uri.Query}} {{r.Header("app_secret")}}"}"""));
        await Record(client.PostAsync($"/api/accounts/{dhan.Id}/connect", Ct));
        await Record(client.DhanCallbackAsync("tid2-" + AccountsApi.Marker, Ct));
        await Record(client.DeleteAsync($"/api/accounts/{dhan.Id}/token", Ct));
        await Record(client.GetAsync("/api/accounts", Ct));
        await Record(client.GetAsync($"/api/accounts/{dhan.Id}", Ct));
        await Record(client.GetAsync($"/api/accounts/{dhan.Id}/activity?take=200", Ct));
        await Record(client.GetAsync("/api/brokers/dhan", Ct));

        texts.Count.ShouldBe(11);
        texts.ShouldAllBe(t => !t.Contains(AccountsApi.Marker, StringComparison.Ordinal));
        texts.ShouldAllBe(t => !t.Contains(LeakClientId, StringComparison.Ordinal));
        var audit = await factory.WithDbAsync(db => db.AccountEvents.AsNoTracking().Select(e => e.Detail + " " + e.Actor).ToListAsync(Ct));
        audit.ShouldContain(d => d.StartsWith("Connected by signing in at Dhan", StringComparison.Ordinal));
        audit.ShouldAllBe(d => !d.Contains(AccountsApi.Marker, StringComparison.Ordinal) && !d.Contains(LeakClientId, StringComparison.Ordinal));
        var rows = await factory.WithDbAsync(db => db.BrokerConnections.AsNoTracking().Select(c => c.LastError + " " + c.BrokerUserName).ToListAsync(Ct));
        rows.ShouldAllBe(r => !r.Contains(AccountsApi.Marker, StringComparison.Ordinal));
        factory.Logs.All.ShouldAllBe(line => !line.Contains(AccountsApi.Marker, StringComparison.Ordinal));
        factory.Logs.All.ShouldAllBe(line => !line.Contains(LeakClientId, StringComparison.Ordinal));

        // The scan above saw this test's own log events (not only older ones): the broker failure and the request log are there.
        factory.Logs.All.ShouldContain(line => line.Contains("consume-consent", StringComparison.Ordinal) && line.Contains("refused", StringComparison.Ordinal));
        factory.Logs.All.ShouldContain(line => line.Contains($"/api/accounts/{dhan.Id}/token", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_broker_client_refuses_every_endpoint_outside_the_allow_list_before_sending()
    {
        var clients = factory.Services.GetRequiredService<IHttpClientFactory>();
        using var dhan = clients.CreateClient(BrokerHttpClientNames.Dhan);

        await Should.ThrowAsync<BrokerEndpointNotAllowedException>(() => dhan.GetAsync(new Uri("https://api.dhan.co/v2/fundlimit"), Ct));
        await Should.ThrowAsync<BrokerEndpointNotAllowedException>(() => dhan.PostAsync(new Uri("https://api.dhan.co/v2/orders"), null, Ct));
        await Should.ThrowAsync<BrokerEndpointNotAllowedException>(() => dhan.GetAsync(new Uri("https://api.dhan.co/v2/positions"), Ct));
        await Should.ThrowAsync<BrokerEndpointNotAllowedException>(() => dhan.GetAsync(new Uri("https://api.dhan.co/v2/profile"), Ct));
        await Should.ThrowAsync<BrokerEndpointNotAllowedException>(() => dhan.GetAsync(new Uri("https://example.invalid/"), Ct));

        factory.Brokers.Requests.ShouldBeEmpty();
        dhan.Timeout.ShouldBe(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task The_callback_is_GET_only_and_never_answers_with_broker_data()
    {
        const string path = "/api/brokers/dhan/callback?tokenId=x";
        var client = (await factory.SignInAsync(Ct)).Client;

        using var post = await client.PostAsync(path, Ct);
        using var get = await client.GetAsync(path, Ct);

        post.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        get.StatusCode.ShouldBe(HttpStatusCode.Found);
        (await get.Content.ReadAsStringAsync(Ct)).ShouldBeEmpty();
        get.Headers.Location!.ToString().ShouldStartWith(ConnectApi.PublicBaseUrl + "/accounts?");
    }
}
