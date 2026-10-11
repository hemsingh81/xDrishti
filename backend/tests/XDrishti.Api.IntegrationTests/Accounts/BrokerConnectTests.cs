using System.Net;
using Microsoft.EntityFrameworkCore;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Accounts;

namespace XDrishti.Api.IntegrationTests.Accounts;

/// <summary>
/// Connecting through Dhan's browser consent login (specs P1-16c, P1-16d) against a fake Dhan: what the API sends, where the
/// callback redirects, and every way a sign-in can go wrong.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class BrokerConnectTests(ApiFactory factory) : IAsyncLifetime
{
    private const string TokenId = "tid-" + AccountsApi.Marker;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await factory.ResetAccountsAsync();
        factory.Brokers.Reset();
    }

    public ValueTask DisposeAsync()
    {
        factory.Brokers.Reset();
        return ValueTask.CompletedTask;
    }

    private List<FakeBrokers.RecordedRequest> Sent(string path) => [.. factory.Brokers.Requests.Where(r => r.Uri.AbsolutePath == path)];

    [Fact]
    public async Task Dhan_consent_login_connects_the_account_and_returns_to_it()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        var account = await client.CreateAccountAsync(AccountsApi.NewAccount(), Ct);

        var start = await client.StartConnectAsync(account.Id, Ct);
        using var callback = await client.DhanCallbackAsync(TokenId, Ct);
        var detail = await client.GetAccountAsync(account.Id, Ct);

        start.AuthorizeUrl.ShouldBe($"https://auth.dhan.co/login/consentApp-login?consentAppId={FakeBrokers.ConsentAppId}");
        var consent = Sent("/app/generate-consent").Single();
        consent.Method.ShouldBe(HttpMethod.Post);
        consent.Uri.AbsoluteUri.ShouldBe($"https://auth.dhan.co/app/generate-consent?client_id={AccountsApi.ClientId}");
        (consent.Header("app_id"), consent.Header("app_secret")).ShouldBe((AccountsApi.ApiKey, AccountsApi.ApiSecret));
        callback.Location().ShouldBe(ConnectApi.Connected(account.Id));
        callback.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var consume = Sent("/app/consumeApp-consent").Single();
        consume.Method.ShouldBe(HttpMethod.Post);
        consume.Uri.AbsoluteUri.ShouldBe($"https://auth.dhan.co/app/consumeApp-consent?tokenId={TokenId}");
        (consume.Header("app_id"), consume.Header("app_secret")).ShouldBe((AccountsApi.ApiKey, AccountsApi.ApiSecret));
        detail.TokenStatus.ShouldBe("connected");
        var connection = detail.Connection;
        (connection.Status, connection.BrokerUserName, connection.BrokerUserId, connection.LastError)
            .ShouldBe(("connected", "Example Holder", "1000••••01", (string?)null));
        connection.ExpiresAt!.Value.ShouldBe(factory.Clock.GetUtcNow().AddHours(20), TimeSpan.FromSeconds(5));
        var activity = await (await client.GetAsync($"/api/accounts/{account.Id}/activity", Ct)).ReadAsync<List<AccountEventView>>(Ct);
        activity[0].Event.ShouldBe("BrokerConnected");
        activity[0].Detail!.ShouldStartWith("Connected by signing in at Dhan as Example Holder; valid until ");
    }

    [Fact]
    public async Task A_replayed_sign_in_is_refused_and_exchanged_once()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        var account = await client.CreateAccountAsync(AccountsApi.NewAccount(), Ct);
        await client.StartConnectAsync(account.Id, Ct);

        using var first = await client.DhanCallbackAsync(TokenId, Ct);
        using var replay = await client.DhanCallbackAsync(TokenId, Ct);

        first.Location().ShouldBe(ConnectApi.Connected(account.Id));
        replay.Location().ShouldBe(ConnectApi.Failed(null, "accounts.connect_expired"));
        Sent("/app/consumeApp-consent").Count.ShouldBe(1);
    }

    [Fact]
    public async Task Dhan_sending_the_owner_back_without_a_tokenId_is_a_cancelled_sign_in()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        var account = await client.CreateAccountAsync(AccountsApi.NewAccount(), Ct);
        await client.StartConnectAsync(account.Id, Ct);

        using var callback = await client.DhanCallbackAsync(null, Ct);

        callback.Location().ShouldBe(ConnectApi.Failed(account.Id, "accounts.connect_cancelled"));
        Sent("/app/consumeApp-consent").ShouldBeEmpty();
    }

    [Fact]
    public async Task Another_session_cannot_complete_the_sign_in()
    {
        var owner = (await factory.SignInElevatedAsync(Ct)).Client;
        var other = (await factory.SignInAsync(Ct)).Client;
        var account = await owner.CreateAccountAsync(AccountsApi.NewAccount(), Ct);
        await owner.StartConnectAsync(account.Id, Ct);

        using var foreign = await other.DhanCallbackAsync(TokenId, Ct);
        using var own = await owner.DhanCallbackAsync(TokenId, Ct);

        foreign.Location().ShouldBe(ConnectApi.Failed(null, "accounts.connect_expired"));
        own.Location().ShouldBe(ConnectApi.Connected(account.Id));
        Sent("/app/consumeApp-consent").Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_sign_in_that_returns_after_ten_minutes_is_refused_without_calling_the_broker()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        var account = await client.CreateAccountAsync(AccountsApi.NewAccount(), Ct);
        await client.StartConnectAsync(account.Id, Ct);
        string location;
        factory.Clock.Advance(TimeSpan.FromMinutes(11));
        try
        {
            using var callback = await client.DhanCallbackAsync(TokenId, Ct);
            location = callback.Location();
        }
        finally
        {
            factory.Clock.Reset();
        }

        location.ShouldBe(ConnectApi.Failed(account.Id, "accounts.connect_expired"));
        Sent("/app/consumeApp-consent").ShouldBeEmpty();
        (await client.GetAccountAsync(account.Id, Ct)).Connection.Status.ShouldBe("not_connected");
    }

    [Fact]
    public async Task A_callback_without_a_session_redirects_instead_of_answering_401()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        var account = await client.CreateAccountAsync(AccountsApi.NewAccount(), Ct);
        await client.StartConnectAsync(account.Id, Ct);

        using var anonymous = await factory.CreateAnonymousClient().DhanCallbackAsync(TokenId, Ct);

        anonymous.Location().ShouldBe(ConnectApi.Failed(null, "auth.unauthenticated"));
        factory.Brokers.Requests.Count.ShouldBe(1); // only the consent of the start
    }

    [Fact]
    public async Task A_sign_in_of_another_client_id_stores_nothing()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        var account = await client.CreateAccountAsync(AccountsApi.NewAccount(), Ct);
        factory.Brokers.DhanClientId = "1000000099";
        await client.StartConnectAsync(account.Id, Ct);

        using var callback = await client.DhanCallbackAsync(TokenId, Ct);
        var detail = await client.GetAccountAsync(account.Id, Ct);

        callback.Location().ShouldBe(ConnectApi.Failed(account.Id, "accounts.connect_mismatch"));
        detail.Connection.Status.ShouldBe("not_connected");
        detail.Connection.LastError.ShouldBe("The Dhan sign-in belongs to another client id; nothing was stored");
    }

    public static TheoryData<string> BrokerFailures => ["400", "401", "500", "503", "timeout", "network", "garbage"];

    private static Func<FakeBrokers.RecordedRequest, HttpResponseMessage> Failure(string kind) => kind switch
    {
        "timeout" => _ => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 15 seconds elapsing."),
        "network" => r => throw new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused (" + r.Uri + ")"),
        "garbage" => _ => FakeBrokers.Json(HttpStatusCode.OK, "<html>" + AccountsApi.Marker + "</html>"),
        _ => r => FakeBrokers.Json(
            (HttpStatusCode)int.Parse(kind, System.Globalization.CultureInfo.InvariantCulture),
            $$"""{"errorType":"Invalid_Authentication","errorCode":"DH-905","errorMessage":"tokenId {{r.Uri.Query}} is not valid"}"""),
    };

    [Theory]
    [MemberData(nameof(BrokerFailures))]
    public async Task A_failing_broker_on_the_callback_ends_in_broker_error_with_a_safe_last_error(string kind)
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        var account = await client.CreateAccountAsync(AccountsApi.NewAccount(), Ct);
        await client.StartConnectAsync(account.Id, Ct);
        factory.Brokers.Override("/app/consumeApp-consent", Failure(kind));

        using var callback = await client.DhanCallbackAsync(TokenId, Ct);
        var detail = await client.GetAccountAsync(account.Id, Ct);

        callback.Location().ShouldBe(ConnectApi.Failed(account.Id, "accounts.broker_error"));
        detail.Connection.Status.ShouldBe("not_connected");
        detail.Connection.LastError!.ShouldStartWith("Sign-in failed: Dhan ");
        detail.Connection.LastError!.ShouldNotContain(AccountsApi.Marker);
        if (kind is "400" or "401")
        {
            detail.Connection.LastError.ShouldBe("Sign-in failed: Dhan refused the request (error DH-905).");
        }
    }

    [Theory]
    [MemberData(nameof(BrokerFailures))]
    public async Task A_failing_broker_on_connect_is_a_safe_502(string kind)
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        var account = await client.CreateAccountAsync(AccountsApi.NewAccount(), Ct);
        factory.Brokers.Override("/app/generate-consent", Failure(kind));

        using var response = await client.PostAsync($"/api/accounts/{account.Id}/connect", Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        (await response.ReadProblemAsync(Ct)).Code.ShouldBe("accounts.broker_error");
        text.ShouldNotContain(AccountsApi.Marker);
        text.ShouldNotContain("errorMessage");
    }

    [Fact]
    public async Task The_26th_Dhan_consent_of_the_day_is_refused_with_429()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        var account = await client.CreateAccountAsync(AccountsApi.NewAccount(), Ct);
        for (var i = 0; i < 25; i++)
        {
            await client.StartConnectAsync(account.Id, Ct);
        }

        using var refused = await client.PostAsync($"/api/accounts/{account.Id}/connect", Ct);

        refused.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        var (code, retryAfter) = await refused.ReadProblemAsync(Ct);
        code.ShouldBe("accounts.consent_limit");
        retryAfter!.Value.ShouldBeInRange(1, 86_400);
        refused.Headers.RetryAfter.ShouldNotBeNull();
        Sent("/app/generate-consent").Count.ShouldBe(25);
    }

    [Fact]
    public async Task Connecting_without_the_API_secret_is_a_conflict()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        var account = await client.CreateAccountAsync(AccountsApi.NewAccount(), Ct);
        await factory.WithDbAsync(db => db.Database.ExecuteSqlAsync($"DELETE FROM accounts.broker_secrets WHERE account_id = {account.Id} AND name = 'api_secret'", Ct));

        using var response = await client.PostAsync($"/api/accounts/{account.Id}/connect", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync(Ct)).Code.ShouldBe("accounts.app_not_configured");
        factory.Brokers.Requests.ShouldBeEmpty();
    }
}
