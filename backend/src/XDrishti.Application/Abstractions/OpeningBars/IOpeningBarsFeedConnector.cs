using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Market.Bars;

namespace XDrishti.Application.Abstractions.OpeningBars;

/// <summary>
/// Opens the opening-minute recorder's own connection to Dhan's market feed (BK-03b): the same allowed endpoint and connector code as the live feed and the tick recorder, another socket with its own packet counts, never the live feed's. A refused token or an
/// unreachable server is a result; only cancellation throws.
/// </summary>
public interface IOpeningBarsFeedConnector
{
    ValueTask<FeedConnectResult> ConnectAsync(DataAccountCredentials credentials, CancellationToken cancellationToken);
}
