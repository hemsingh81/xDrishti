using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Domain.Accounts;

namespace XDrishti.Infrastructure.Brokers;

/// <summary>Reads the day's trade book (<c>GET /v2/trades</c>, spec P11-01, ADR 0149) through <see cref="DhanBookClient"/> and maps it with <see cref="DhanTradeBookMapper"/>. Read only.</summary>
internal sealed class DhanTradeBookClient(IHttpClientFactory httpClients, DhanHistoryThrottle throttle, ILogger<DhanTradeBookClient> logger) : IBrokerTradeBook
{
    private readonly DhanBookClient _book = new(httpClients, throttle, logger);

    public ValueTask<BrokerResponse<LiveBookRead<LiveBrokerFill>>> ReadAsync(SecretValue accessToken, CancellationToken cancellationToken) =>
        _book.ReadAsync(BrokerEndpoints.DhanTradeBook, "trade book", accessToken, DhanTradeBookMapper.Map, cancellationToken);
}
