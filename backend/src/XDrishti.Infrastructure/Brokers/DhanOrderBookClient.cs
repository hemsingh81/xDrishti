using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Domain.Accounts;

namespace XDrishti.Infrastructure.Brokers;

/// <summary>Reads the day's order book (<c>GET /v2/orders</c>, no id, no query; spec P11-01, ADR 0149) through <see cref="DhanBookClient"/> and maps it with <see cref="DhanOrderBookMapper"/>. Read only: it sends nothing but the GET.</summary>
internal sealed class DhanOrderBookClient(IHttpClientFactory httpClients, DhanHistoryThrottle throttle, ILogger<DhanOrderBookClient> logger) : IBrokerOrderBook
{
    private readonly DhanBookClient _book = new(httpClients, throttle, logger);

    public ValueTask<BrokerResponse<LiveBookRead<LiveBrokerOrder>>> ReadAsync(SecretValue accessToken, CancellationToken cancellationToken) =>
        _book.ReadAsync(BrokerEndpoints.DhanOrderBook, "order book", accessToken, DhanOrderBookMapper.Map, cancellationToken);
}
