using XDrishti.Application.Abstractions.Portfolio;

namespace XDrishti.Application.Tests.Portfolio.Groups;

/// <summary>The master's sectors and the live baskets' members as plain dictionaries, counting how often each is read (one read per request is the rule).</summary>
internal sealed class FakeHoldingClassifications : IHoldingClassifications
{
    public Dictionary<Guid, string> Sectors { get; } = [];

    /// <summary>Instrument to the live baskets that hold it.</summary>
    public Dictionary<Guid, List<BasketRef>> Baskets { get; } = [];

    public int SectorReads { get; private set; }

    public int BasketReads { get; private set; }

    public ValueTask<IReadOnlyDictionary<Guid, string>> SectorsAsync(IReadOnlyCollection<Guid> instrumentIds, CancellationToken cancellationToken)
    {
        SectorReads++;
        return ValueTask.FromResult<IReadOnlyDictionary<Guid, string>>(Sectors.Where(s => instrumentIds.Contains(s.Key)).ToDictionary(s => s.Key, s => s.Value));
    }

    public ValueTask<IReadOnlyDictionary<Guid, IReadOnlyList<BasketRef>>> BasketsAsync(IReadOnlyCollection<Guid> instrumentIds, CancellationToken cancellationToken)
    {
        BasketReads++;
        return ValueTask.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<BasketRef>>>(
            Baskets.Where(b => instrumentIds.Contains(b.Key)).ToDictionary(b => b.Key, b => (IReadOnlyList<BasketRef>)[.. b.Value]));
    }
}
