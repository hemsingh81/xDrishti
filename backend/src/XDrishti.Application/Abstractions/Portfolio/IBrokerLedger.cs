using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Domain.Accounts;

namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>
/// The ledger of a broker account (ADR 0025): the cash movements between two India dates (inclusive). Only reads, with the same promises as <see cref="IBrokerTrades"/>: no token or raw answer is logged or returned, failures are
/// <see cref="BrokerResponse{T}"/>, a refused token is <see cref="BrokerFailureKind.Unauthorized"/> and Dhan's "no data" code (DH-907) is an empty read marked <see cref="BrokerLedgerRead.NoData"/>.
/// </summary>
public interface IBrokerLedger
{
    ValueTask<BrokerResponse<BrokerLedgerRead>> FetchAsync(SecretValue accessToken, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
