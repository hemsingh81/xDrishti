using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Domain.Accounts;
using XDrishti.Infrastructure.Brokers;

namespace XDrishti.Infrastructure.Tests.Brokers;

/// <summary>What the Dhan connector sends and how it reads Dhan's answers (made-up values; secrets carry a marker).</summary>
public sealed class DhanConnectorTests : IDisposable
{
    private const string Marker = "FAKE-SECRET-MARKER-DO-NOT-LEAK";

    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    private readonly StubBroker _broker = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _broker.Dispose();

    private static SecretValue Secret(string text) => SecretValue.TryCreate(text, out var value, out _) ? value : throw new ArgumentException(text);

    private static BrokerAppCredentials App => new(Secret("key-" + Marker), Secret("secret-" + Marker));

    private DhanConnector Connector => new(_broker, NullLogger<DhanConnector>.Instance);

    [Fact]
    public async Task Starting_asks_for_a_consent_with_the_app_headers_and_returns_the_login_page()
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, """{"consentAppId":"consent-1/2","consentAppStatus":"GENERATED","status":"success"}""");

        var result = await Connector.StartLoginAsync(new LoginStartRequest(App, "1000000001"), Ct);

        result.Value.AbsoluteUri.ShouldBe("https://auth.dhan.co/login/consentApp-login?consentAppId=consent-1%2F2");
        var request = _broker.Requests.Single();
        request.Method.ShouldBe(HttpMethod.Post);
        request.Uri.AbsoluteUri.ShouldBe("https://auth.dhan.co/app/generate-consent?client_id=1000000001");
        request.Headers["app_id"].ShouldBe("key-" + Marker);
        request.Headers["app_secret"].ShouldBe("secret-" + Marker);
    }

    [Fact]
    public async Task Completing_consumes_the_token_id_and_reads_token_holder_and_expiry()
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, $$"""
            {"dhanClientId":"1000000001","dhanClientName":"Example Holder","dhanClientUcc":"ABCD1234","givenPowerOfAttorney":false,
             "accessToken":"token-{{Marker}}","expiryTime":"2026-10-12T09:15:00"}
            """);

        var result = await Connector.CompleteLoginAsync(new LoginCompletion(App, Secret("tid-" + Marker)), Ct);

        var token = result.Value;
        token.AccessToken.Reveal().ShouldBe("token-" + Marker);
        token.BrokerUserId.ShouldBe("1000000001");
        token.BrokerUserName.ShouldBe("Example Holder");
        token.ExpiresAt.ShouldBe(new DateTimeOffset(2026, 10, 12, 9, 15, 0, Ist));
        var request = _broker.Requests.Single();
        request.Uri.AbsoluteUri.ShouldBe($"https://auth.dhan.co/app/consumeApp-consent?tokenId=tid-{Marker}");
        request.Headers["app_id"].ShouldBe("key-" + Marker);
        request.Headers["app_secret"].ShouldBe("secret-" + Marker);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, """{"errorType":"Invalid_Authentication","errorCode":"DH-901","errorMessage":"Client ID or user generated access token is invalid or expired."}""", BrokerFailureKind.Rejected, "DH-901")]
    [InlineData(HttpStatusCode.BadRequest, """{"errorCode":"has spaces and is long enough to be a message"}""", BrokerFailureKind.Rejected, null)]
    [InlineData(HttpStatusCode.BadRequest, """{"errorCode":"FAKE-SECRET-MARKER-DO-NOT-LEAK"}""", BrokerFailureKind.Rejected, null)] // an echo, not a Dhan code
    [InlineData(HttpStatusCode.BadRequest, """{"errorCode":"UDAPI100057"}""", BrokerFailureKind.Rejected, null)] // not Dhan's form
    [InlineData(HttpStatusCode.InternalServerError, """{"errorCode":"DH-908"}""", BrokerFailureKind.Unavailable, "DH-908")]
    [InlineData(HttpStatusCode.BadGateway, "<html>bad gateway</html>", BrokerFailureKind.Unavailable, null)]
    [InlineData(HttpStatusCode.Found, "", BrokerFailureKind.Unavailable, null)]
    [InlineData(HttpStatusCode.OK, """{"status":"failure","errorCode":"DH-905"}""", BrokerFailureKind.Rejected, "DH-905")]
    [InlineData(HttpStatusCode.OK, """{"status":"success"}""", BrokerFailureKind.Unavailable, null)]
    [InlineData(HttpStatusCode.OK, "not json", BrokerFailureKind.Unavailable, null)]
    public async Task Failures_are_mapped_to_refused_or_unavailable_with_the_broker_code_only(HttpStatusCode status, string body, BrokerFailureKind kind, string? code)
    {
        _broker.Respond = _ => StubBroker.Json(status, body);

        var result = await Connector.CompleteLoginAsync(new LoginCompletion(App, Secret("tid")), Ct);

        result.IsSuccess.ShouldBeFalse();
        result.Failure.ShouldBe(new BrokerFailure(kind, code));
    }

    [Fact]
    public async Task A_timeout_or_network_failure_is_unavailable()
    {
        _broker.Respond = _ => throw new TaskCanceledException("timed out");
        var timedOut = await Connector.CompleteLoginAsync(new LoginCompletion(App, Secret("t")), Ct);
        _broker.Respond = _ => throw new HttpRequestException(HttpRequestError.ConnectionError, "refused https://auth.dhan.co/app/consumeApp-consent");
        var unreachable = await Connector.CompleteLoginAsync(new LoginCompletion(App, Secret("t")), Ct);

        timedOut.Failure.ShouldBe(new BrokerFailure(BrokerFailureKind.Unavailable));
        unreachable.Failure.ShouldBe(new BrokerFailure(BrokerFailureKind.Unavailable));
    }

    [Fact]
    public async Task A_cancelled_request_is_not_hidden()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        _broker.Respond = _ => throw new TaskCanceledException();

        await Should.ThrowAsync<OperationCanceledException>(async () => await Connector.CompleteLoginAsync(new LoginCompletion(App, Secret("t")), cancelled.Token));
    }
}
