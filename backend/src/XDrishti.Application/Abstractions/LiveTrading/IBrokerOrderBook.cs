using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Domain.Accounts;

namespace XDrishti.Application.Abstractions.LiveTrading;

/// <summary>The day's order book of a broker account (spec P11-01, ADR 0149): every order of today and its state, one answer, no query. Only reads; the name says "book", and no member places, modifies or cancels anything. Failures as for <see cref="IBrokerTradeBook"/>.</summary>
public interface IBrokerOrderBook
{
    ValueTask<BrokerResponse<LiveBookRead<LiveBrokerOrder>>> ReadAsync(SecretValue accessToken, CancellationToken cancellationToken);
}
