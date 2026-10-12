using XDrishti.Domain.Market;

namespace XDrishti.Application.Abstractions.Market;

/// <summary>Persistence port for the tracked instruments. New and changed rows are saved by <see cref="IMarketUnitOfWork"/>.</summary>
public interface ITrackedInstrumentStore
{
    /// <summary>The master-list rows with these ids, retired ones included; ids that do not exist are left out. For reading only.</summary>
    ValueTask<IReadOnlyList<TrackableInstrument>> FindInstrumentsAsync(IReadOnlyCollection<Guid> instrumentIds, CancellationToken cancellationToken);

    /// <summary>The tracking rows of these instruments (active or not), tracked by the context so that changes are saved; instruments never tracked have none.</summary>
    ValueTask<IReadOnlyList<TrackedInstrument>> FindAsync(IReadOnlyCollection<Guid> instrumentIds, CancellationToken cancellationToken);

    void Add(TrackedInstrument tracked);

    /// <summary>
    /// States that the change being collected depends on this row as it was read (for example that an instrument is tracked): the save is refused
    /// (<see cref="IMarketUnitOfWork.TrySaveAsync"/> returns false) if the row was changed by someone else in the meantime, even though this request does not change it.
    /// </summary>
    void RelyOn(TrackedInstrument tracked);

    /// <summary>How many instruments are tracked right now (retired ones included).</summary>
    ValueTask<int> CountActiveAsync(CancellationToken cancellationToken);
}
