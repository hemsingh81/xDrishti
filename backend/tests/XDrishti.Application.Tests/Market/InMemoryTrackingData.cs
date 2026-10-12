using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Tests.Market;

/// <summary>The master list, the tracking rows, the unit of work and the event log in memory. Adding a row only shows up in the store after a save, as with the database.</summary>
internal sealed class InMemoryTrackingData : ITrackedInstrumentStore, IMarketUnitOfWork, IAccountEventLog, IInstrumentQueries
{
    private readonly List<TrackedInstrument> _pending = [];

    public List<TrackableInstrument> Instruments { get; } = [];

    public List<TrackedInstrument> Tracked { get; } = [];

    public List<AccountEvent> Events { get; } = [];

    /// <summary>The next saves that are refused as a uniqueness collision, each one first adding the rows another request would have added.</summary>
    public Queue<Action> Collisions { get; } = new();

    public int Saves { get; private set; }

    public ResolveCandidate[] ResolveRows { get; set; } = [];

    public (IReadOnlyCollection<string> Symbols, IReadOnlyCollection<string> Isins)? LastResolve { get; private set; }

    /// <summary>A master-list row.</summary>
    public TrackableInstrument AddInstrument(string symbol, bool retired = false)
    {
        var instrument = new TrackableInstrument(Guid.CreateVersion7(), symbol, retired);
        Instruments.Add(instrument);
        return instrument;
    }

    public ValueTask<IReadOnlyList<TrackableInstrument>> FindInstrumentsAsync(IReadOnlyCollection<Guid> instrumentIds, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<TrackableInstrument>>([.. Instruments.Where(i => instrumentIds.Contains(i.Id))]);

    public ValueTask<IReadOnlyList<TrackedInstrument>> FindAsync(IReadOnlyCollection<Guid> instrumentIds, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<TrackedInstrument>>([.. Tracked.Where(t => instrumentIds.Contains(t.Id))]);

    public void Add(TrackedInstrument tracked) => _pending.Add(tracked);

    /// <summary>The rows the collected change depends on without changing them; the next save refuses when one of them was changed since (see <see cref="Collisions"/>).</summary>
    public List<TrackedInstrument> Relied { get; } = [];

    public void RelyOn(TrackedInstrument tracked) => Relied.Add(tracked);

    /// <summary>The rows the last refused save depended on (<see cref="Relied"/> as it was when the save was refused).</summary>
    public IReadOnlyList<TrackedInstrument> ReliedOnWhenRefused { get; private set; } = [];

    public ValueTask<int> CountActiveAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Tracked.Count(t => t.Active));

    public ValueTask SaveAsync(CancellationToken cancellationToken)
    {
        Saves++;
        Tracked.AddRange(_pending);
        _pending.Clear();
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> TrySaveAsync(CancellationToken cancellationToken)
    {
        Saves++;
        if (Collisions.TryDequeue(out var concurrentRequest))
        {
            DiscardPending();
            concurrentRequest();
            return ValueTask.FromResult(false);
        }

        Tracked.AddRange(_pending);
        _pending.Clear();
        Relied.Clear();
        return ValueTask.FromResult(true);
    }

    /// <summary>Drops what was added since the last save, as a refused save does.</summary>
    public void DiscardPending()
    {
        ReliedOnWhenRefused = [.. Relied];
        _pending.Clear();
        Relied.Clear();
    }

    public void Record(AccountEvent accountEvent) => Events.Add(accountEvent);

    public ValueTask<IReadOnlyList<AccountEvent>> ListForAccountAsync(Guid accountId, int take, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<AccountEvent>>([]);

    public ValueTask<IReadOnlyList<ResolveCandidate>> FindForResolveAsync(
        IReadOnlyCollection<string> symbols, IReadOnlyCollection<string> isins, CancellationToken cancellationToken)
    {
        LastResolve = (symbols, isins);
        return ValueTask.FromResult<IReadOnlyList<ResolveCandidate>>(ResolveRows);
    }

    public ValueTask<XDrishti.Application.Market.Search.InstrumentPage> SearchAsync(XDrishti.Application.Market.Search.InstrumentSearch search, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public ValueTask<XDrishti.Application.Market.Search.InstrumentFacets> FacetsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<XDrishti.Application.Market.Search.InstrumentDetail?> GetAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
}
