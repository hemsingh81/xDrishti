using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Ticks;

namespace XDrishti.Infrastructure.Brokers;

/// <summary>
/// The trade-tick recorder's connector (P7-16b decision 1, criterion 1): a <b>second, separate</b> <see cref="DhanFeedConnector"/> - the same allowed endpoint <c>wss://api-feed.dhan.co</c>, the same allow-list check
/// (<see cref="BrokerEndpoints.IsAllowedWebSocket"/>) before any connection, the same parser and the same refusal of everything else - with packet counts of its own, so the live feed's statistics and connection are never touched. No new allow-list
/// entry. <paramref name="connectTo"/> exists for the tests only (a local server replaces the authority after the check on the real URL).
/// </summary>
internal sealed class TickFeedConnector(TimeProvider clock, ILoggerFactory loggers, Uri? connectTo = null) : ITickFeedConnector
{
    private readonly DhanFeedConnector _inner = new(clock, loggers, connectTo, new FeedPacketStats());

    public ValueTask<FeedConnectResult> ConnectAsync(DataAccountCredentials credentials, CancellationToken cancellationToken) => _inner.ConnectAsync(credentials, cancellationToken);
}
