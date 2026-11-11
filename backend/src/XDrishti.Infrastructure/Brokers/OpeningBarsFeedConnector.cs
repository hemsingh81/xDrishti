using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.OpeningBars;

namespace XDrishti.Infrastructure.Brokers;

/// <summary>
/// The opening-minute recorder's connector (BK-03b): a <b>separate</b> <see cref="DhanFeedConnector"/> - the same allowed endpoint <c>wss://api-feed.dhan.co</c>, the same allow-list check (<see cref="BrokerEndpoints.IsAllowedWebSocket"/>) before any
/// connection, the same parser and the same refusal of everything else - with packet counts of its own, so the live feed's statistics, connection and subscription list are never touched. The tick and depth recorders already open their own sockets the same way, so
/// a second connection is within what the owner's plan carries (the 805 disconnect code is handled as a refusal if it is not). No new allow-list entry. <paramref name="connectTo"/> exists for the tests only.
/// </summary>
internal sealed class OpeningBarsFeedConnector(TimeProvider clock, ILoggerFactory loggers, Uri? connectTo = null) : IOpeningBarsFeedConnector
{
    private readonly DhanFeedConnector _inner = new(clock, loggers, connectTo, new FeedPacketStats());

    public ValueTask<FeedConnectResult> ConnectAsync(DataAccountCredentials credentials, CancellationToken cancellationToken) => _inner.ConnectAsync(credentials, cancellationToken);
}
