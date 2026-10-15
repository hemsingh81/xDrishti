using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;

namespace XDrishti.Infrastructure.Brokers;

/// <summary>
/// Reads the account's trade history from DhanHQ v2 (<c>GET /v2/trades/{from-date}/{to-date}/{page}</c>, pages from 0, header <c>access-token</c>; ADR 0025). Only that path shape is allowed by <see cref="BrokerEndpointGuard"/>; no redirects,
/// 30-second timeout, answers up to 5 MB, calls spaced by <see cref="DhanHistoryThrottle"/>. The token is only put into the request header and neither it, the URL nor Dhan's answer is ever logged or put into an error. Dhan's data error
/// DH-907 ("incorrect parameters or no data present"), whether Dhan sends it with a 400 or in the body of a 200, is an empty page marked <see cref="BrokerTradePage.NoData"/> - not a failure; what it means (the end of the history, an empty day, or a request
/// Dhan did not like) depends on where it comes and is decided by the reader. A refused token (401, 403, DH-901), a rate limit (429, DH-904), a 5xx or a timeout are failures and are never read as "no data".
/// The rows are mapped by <see cref="DhanTradeMapper"/>. The answer is Dhan's array of trades (an object holding it under <c>data</c> is tolerated).
/// </summary>
internal sealed class DhanTradesClient(IHttpClientFactory httpClients, DhanHistoryThrottle throttle, ILogger<DhanTradesClient> logger) : IBrokerTrades
{
    /// <summary>Dhan's documented data error code: "incorrect parameters or no data present". The dates are ours and valid, so it most likely means "no data", but it is not certain (see <see cref="BrokerTradePage.NoData"/>).</summary>
    internal const string NoDataCode = "DH-907";

    /// <summary>More rows than a page of trades has; a bigger answer is treated as unusable.</summary>
    internal const int MaxRows = 20_000;

    private const string Operation = "trade history";

    public int MapperVersion => DhanTradeMapper.Version;

    public async ValueTask<BrokerResponse<BrokerTradePage>> FetchPageAsync(SecretValue accessToken, DateOnly from, DateOnly to, int page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accessToken);
        await throttle.WaitAsync(cancellationToken);
        using var message = new HttpRequestMessage(HttpMethod.Get, BrokerEndpoints.BuildTradeHistoryUri(from, to, page));
        message.Headers.TryAddWithoutValidation("access-token", accessToken.Reveal());
        using var client = httpClients.CreateClient(BrokerHttpClientNames.DhanHistory);
        var answer = await BrokerHttp.SendAsync(client, message, Operation, logger, cancellationToken, tokenCall: true);
        if (!answer.IsSuccess)
        {
            var failure = answer.Failure!;
            return failure is { Kind: BrokerFailureKind.Rejected, BrokerCode: NoDataCode }
                ? BrokerResponse.Ok(BrokerTradePage.NoDataAnswer)
                : BrokerResponse.Fail<BrokerTradePage>(failure);
        }

        var rows = DhanJson.Rows(answer.Value);
        if (rows is { } list && list.GetArrayLength() <= MaxRows)
        {
            return BrokerResponse.Ok(DhanTradeMapper.Map(list));
        }

        // The same code in the body of a 200 means the same as with a 400.
        return rows is null && BrokerHttp.ErrorCode(answer.Value) == NoDataCode
            ? BrokerResponse.Ok(BrokerTradePage.NoDataAnswer)
            : BrokerHttp.Unexpected<BrokerTradePage>(answer.Value, Operation, logger);
    }
}
