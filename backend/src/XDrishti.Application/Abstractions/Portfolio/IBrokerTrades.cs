using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Domain.Accounts;

namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>
/// The trade history of a broker account (ADR 0025): the executed trades between two India dates, one page at a time (pages count from 0; an empty page is the end). Only reads. Implementations never log, return or put into exceptions the token or the
/// broker's raw answer, and report failures as <see cref="BrokerResponse{T}"/> instead of throwing. A token the broker refuses is <see cref="BrokerFailureKind.Unauthorized"/>; Dhan's "no data" code (DH-907, as an error status or in a 200 body) is an empty page marked <see cref="BrokerTradePage.NoData"/>, not a failure - what it means is the reader's call.
/// </summary>
public interface IBrokerTrades
{
    ValueTask<BrokerResponse<BrokerTradePage>> FetchPageAsync(SecretValue accessToken, DateOnly from, DateOnly to, int page, CancellationToken cancellationToken);
}
