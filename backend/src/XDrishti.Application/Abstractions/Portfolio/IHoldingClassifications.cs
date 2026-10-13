namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>A live (not archived) basket by id and name.</summary>
public sealed record BasketRef(Guid Id, string Name);

/// <summary>
/// How the instrument master and the baskets classify the instruments of the holdings, for grouping the portfolio by sector or basket (ADR 0021). Each method is one read for all the instruments asked for,
/// never one per holding or per group.
/// </summary>
public interface IHoldingClassifications
{
    /// <summary>The master's sector of each instrument that has one (instruments without a sector, or unknown ones, are missing from the answer).</summary>
    ValueTask<IReadOnlyDictionary<Guid, string>> SectorsAsync(IReadOnlyCollection<Guid> instrumentIds, CancellationToken cancellationToken);

    /// <summary>
    /// The live baskets that hold each instrument now (current members of baskets that are not archived; the portfolio's system baskets are a view of the buckets and not stored here). Instruments in no basket are missing from the answer.
    /// </summary>
    ValueTask<IReadOnlyDictionary<Guid, IReadOnlyList<BasketRef>>> BasketsAsync(IReadOnlyCollection<Guid> instrumentIds, CancellationToken cancellationToken);
}
