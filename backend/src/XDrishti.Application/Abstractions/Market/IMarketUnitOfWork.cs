namespace XDrishti.Application.Abstractions.Market;

/// <summary>
/// Saves what the tracked-instrument store and the event log have collected, atomically. The scoped database context collects the changes of every
/// store of the request, so a use case that also changes other tables (for example baskets) saves them all with one call.
/// </summary>
public interface IMarketUnitOfWork
{
    /// <summary>Saves everything collected, atomically (throws when the database refuses).</summary>
    ValueTask SaveAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Saves everything collected; false (and the collected changes discarded) when the database refused it because a concurrent change broke a
    /// uniqueness rule (the same instrument tracked by two requests at once) or a row had been changed since it was read (a basket's revision, a tracking row's version).
    /// The caller reads again and starts over.
    /// </summary>
    ValueTask<bool> TrySaveAsync(CancellationToken cancellationToken);
}
