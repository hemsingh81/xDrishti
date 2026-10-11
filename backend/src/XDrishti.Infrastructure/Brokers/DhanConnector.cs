using System.Text.Json;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Domain.Accounts;

namespace XDrishti.Infrastructure.Brokers;

/// <summary>
/// DhanHQ v2 authentication for individuals (API key &amp; secret consent flow, ADR 0009): generate-consent → browser login →
/// consumeApp-consent. Nothing else of Dhan is called. The consume URL carries the single-use tokenId: like every secret it is never logged.
/// </summary>
internal sealed class DhanConnector(IHttpClientFactory httpClients, ILogger<DhanConnector> logger) : IBrokerConnector
{
    public async ValueTask<BrokerResponse<Uri>> StartLoginAsync(LoginStartRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri($"{BrokerEndpoints.DhanGenerateConsent}?client_id={Uri.EscapeDataString(request.ClientId)}"));
        AddApp(message, request.App);
        var answer = await SendAsync(message, "generate-consent", cancellationToken);
        if (!answer.IsSuccess)
        {
            return BrokerResponse.Fail<Uri>(answer.Failure!);
        }

        var consentAppId = BrokerHttp.Text(answer.Value, "consentAppId");
        return consentAppId is null
            ? BrokerHttp.Unexpected<Uri>(answer.Value, "generate-consent", logger)
            : BrokerResponse.Ok(new Uri($"{BrokerEndpoints.DhanLoginPage}?consentAppId={Uri.EscapeDataString(consentAppId)}"));
    }

    public async ValueTask<BrokerResponse<BrokerToken>> CompleteLoginAsync(LoginCompletion completion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(completion);
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri($"{BrokerEndpoints.DhanConsumeConsent}?tokenId={Uri.EscapeDataString(completion.Code.Reveal())}"));
        AddApp(message, completion.App);
        var answer = await SendAsync(message, "consume-consent", cancellationToken);
        return answer.IsSuccess ? Token(answer.Value) : BrokerResponse.Fail<BrokerToken>(answer.Failure!);
    }

    private static void AddApp(HttpRequestMessage message, BrokerAppCredentials app)
    {
        message.Headers.TryAddWithoutValidation("app_id", app.ApiKey.Reveal());
        message.Headers.TryAddWithoutValidation("app_secret", app.ApiSecret.Reveal());
    }

    /// <summary>consumeApp-consent answers with accessToken, expiryTime, dhanClientId and dhanClientName.</summary>
    private BrokerResponse<BrokerToken> Token(JsonElement body)
    {
        var clientId = BrokerHttp.Text(body, "dhanClientId");
        if (clientId is null || !SecretValue.TryCreate(BrokerHttp.Text(body, "accessToken"), out var token, out _))
        {
            return BrokerHttp.Unexpected<BrokerToken>(body, "consume-consent", logger);
        }

        var expiry = DhanTime.Parse(BrokerHttp.Text(body, "expiryTime"));
        return BrokerResponse.Ok(new BrokerToken(token, clientId, BrokerHttp.Text(body, "dhanClientName"), expiry));
    }

    private async ValueTask<BrokerResponse<JsonElement>> SendAsync(HttpRequestMessage message, string operation, CancellationToken cancellationToken)
    {
        using var client = httpClients.CreateClient(BrokerHttpClientNames.Dhan);
        return await BrokerHttp.SendAsync(client, message, operation, logger, cancellationToken);
    }
}
