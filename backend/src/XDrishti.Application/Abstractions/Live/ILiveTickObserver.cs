namespace XDrishti.Application.Abstractions.Live;

/// <summary>
/// A listener on the firm ticks of each live flush (spec P7-20, ADR 0115), so the Portfolio side of the feed does not know the Planning side. An observer is registered only when its feature is enabled. It is called after the prices are written and must
/// swallow and log its own failure: a failure here must never cost a price, and the flush also guards the call.
/// </summary>
public interface ILiveTickObserver
{
    /// <summary>The latest firm tick (09:15 India time or later) of each flushed instrument.</summary>
    ValueTask ObserveAsync(IReadOnlyList<LivePrice> firmTicks, CancellationToken cancellationToken);
}
