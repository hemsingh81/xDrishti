using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;

namespace XDrishti.Infrastructure.Brokers;

/// <summary>
/// Reads the account's ledger from DhanHQ v2 (<c>GET /v2/ledger?from-date=…&amp;to-date=…</c>, header <c>access-token</c>; ADR 0025) with the same promises as <see cref="DhanTradesClient"/>: only that path with exactly those two query keys is allowed by
/// <see cref="BrokerEndpointGuard"/>; no redirects, 30-second timeout, answers up to 5 MB, spaced calls, the token only in the header, nothing of the answer logged. DH-907 (with a 400 or in the body of a 200) is an empty read marked <see cref="BrokerLedgerRead.NoData"/>, which the reader interprets; a refused token, a rate limit, a 5xx or a timeout are failures.
/// The rows are mapped by <see cref="DhanLedgerMapper"/>.
/// </summary>
internal sealed class DhanLedgerClient(IHttpClientFactory httpClients, DhanHistoryThrottle throttle, ILogger<DhanLedgerClient> logger) : IBrokerLedger
{
    /// <summary>More rows than a month of a ledger has; a bigger answer is treated as unusable.</summary>
    internal const int MaxRows = 50_000;

    private const string Operation = "ledger";

    public int MapperVersion => DhanLedgerMapper.Version;

    public async ValueTask<BrokerResponse<BrokerLedgerRead>> FetchAsync(SecretValue accessToken, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accessToken);
        await throttle.WaitAsync(cancellationToken);
        using var message = new HttpRequestMessage(HttpMethod.Get, BrokerEndpoints.BuildLedgerUri(from, to));
        message.Headers.TryAddWithoutValidation("access-token", accessToken.Reveal());
        using var client = httpClients.CreateClient(BrokerHttpClientNames.DhanHistory);
        var answer = await BrokerHttp.SendAsync(client, message, Operation, logger, cancellationToken, tokenCall: true);
        if (!answer.IsSuccess)
        {
            var failure = answer.Failure!;
            return failure is { Kind: BrokerFailureKind.Rejected, BrokerCode: DhanTradesClient.NoDataCode }
                ? BrokerResponse.Ok(BrokerLedgerRead.NoDataAnswer)
                : BrokerResponse.Fail<BrokerLedgerRead>(failure);
        }

        var rows = DhanJson.Rows(answer.Value);
        if (rows is { } list && list.GetArrayLength() <= MaxRows)
        {
            return BrokerResponse.Ok(DhanLedgerMapper.Map(list));
        }

        // Dhan's published sample is a single row as an object: one row.
        if (rows is null && DhanLedgerMapper.SingleRow(answer.Value) is { } single)
        {
            return BrokerResponse.Ok(DhanLedgerMapper.Map(single));
        }

        // The same code in the body of a 200 means the same as with a 400.
        return rows is null && BrokerHttp.ErrorCode(answer.Value) == DhanTradesClient.NoDataCode
            ? BrokerResponse.Ok(BrokerLedgerRead.NoDataAnswer)
            : BrokerHttp.Unexpected<BrokerLedgerRead>(answer.Value, Operation, logger);
    }
}
