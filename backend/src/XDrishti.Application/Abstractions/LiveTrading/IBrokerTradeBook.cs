using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Domain.Accounts;

namespace XDrishti.Application.Abstractions.LiveTrading;

/// <summary>
/// The day's trade book of a broker account (spec P11-01, ADR 0149): every executed trade of today, one answer, no query. Only reads. Implementations never log, return or put into exceptions the token or the raw answer, and report failures as
/// <see cref="BrokerResponse{T}"/>. A refused token is <see cref="BrokerFailureKind.Unauthorized"/>, Dhan's rate limit <see cref="BrokerFailureKind.RateLimited"/>; an empty day is an empty read, not a failure.
/// </summary>
public interface IBrokerTradeBook
{
    ValueTask<BrokerResponse<LiveBookRead<LiveBrokerFill>>> ReadAsync(SecretValue accessToken, CancellationToken cancellationToken);
}
