using System.Text.Json;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Domain.Accounts;

namespace XDrishti.Infrastructure.Brokers;

/// <summary>
/// The one GET of a day's book, shared by the trade book and the order book clients (spec P11-01, ADR 0149): the endpoint is fixed by the caller from <see cref="BrokerEndpoints"/> and re-checked by <see cref="BrokerEndpointGuard"/>; header <c>access-token</c>, no query,
/// no redirect, 30 s, 5 MB, calls spaced by <see cref="DhanHistoryThrottle"/>. The token is only put into the header; neither it, the URL nor Dhan's answer is logged or put into an error. Dhan's "no data" code (DH-907) is an empty read; a refused token,
/// a rate limit, a 5xx or a timeout is a failure, never an empty day. Read only.
/// </summary>
internal sealed class DhanBookClient(IHttpClientFactory httpClients, DhanHistoryThrottle throttle, ILogger logger)
{
    internal const string NoDataCode = "DH-907";
    internal const int MaxRows = 20_000;

    public async ValueTask<BrokerResponse<LiveBookRead<T>>> ReadAsync<T>(Uri endpoint, string operation, SecretValue accessToken, Func<JsonElement, LiveBookRead<T>> map, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accessToken);
        await throttle.WaitAsync(cancellationToken);
        using var message = new HttpRequestMessage(HttpMethod.Get, endpoint);
        message.Headers.TryAddWithoutValidation("access-token", accessToken.Reveal());
        using var client = httpClients.CreateClient(BrokerHttpClientNames.DhanHistory);
        var answer = await BrokerHttp.SendAsync(client, message, operation, logger, cancellationToken, tokenCall: true);
        if (!answer.IsSuccess)
        {
            var failure = answer.Failure!;
            return failure is { Kind: BrokerFailureKind.Rejected, BrokerCode: NoDataCode }
                ? BrokerResponse.Ok(LiveBookReads.Empty<T>())
                : BrokerResponse.Fail<LiveBookRead<T>>(failure);
        }

        var rows = DhanJson.Rows(answer.Value);
        if (rows is { } list && list.GetArrayLength() <= MaxRows)
        {
            return BrokerResponse.Ok(map(list));
        }

        return rows is null && BrokerHttp.ErrorCode(answer.Value) == NoDataCode
            ? BrokerResponse.Ok(LiveBookReads.Empty<T>())
            : BrokerHttp.Unexpected<LiveBookRead<T>>(answer.Value, operation, logger);
    }
}
