using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Market.Bars;

namespace XDrishti.Application.Abstractions.Ticks;

/// <summary>
/// Opens the trade-tick recorder's own connection to Dhan's market feed (P7-16b decision 1): the same allowed endpoint and the same connector code as the live feed, but another socket with its own packet counts, never the live feed's.
/// The implementation checks the endpoint against the allow-list before anything is opened.
/// </summary>
public interface ITickFeedConnector
{
    /// <summary>Connects. A refused token or an unreachable server is a result, not an exception; only cancellation throws.</summary>
    ValueTask<FeedConnectResult> ConnectAsync(DataAccountCredentials credentials, CancellationToken cancellationToken);
}
