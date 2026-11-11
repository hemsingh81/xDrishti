using XDrishti.Domain.Lab;

namespace XDrishti.Application.Abstractions.Lab;

/// <summary>The stored Lab tests of a strategy family that named a basket (BK-11): the ledger row joined to its run's universe. A read.</summary>
public interface IUniverseTrialSource
{
    /// <summary>One entry per ledger row of a run of the strategy whose Lab run named a basket.</summary>
    ValueTask<IReadOnlyList<UniverseTrial>> ListBasketTestsAsync(string strategyId, CancellationToken cancellationToken);
}
