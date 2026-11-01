using XDrishti.Domain.Options;

namespace XDrishti.Application.Abstractions.Options;

/// <summary>
/// Writes the option archive's reference data (spec P14-01): <c>options.contracts</c> (new rows inserted, <c>last_seen_on</c> advanced, never deleted) and
/// <c>options.lot_sizes</c> (missing seeds, and a <c>scrip_master</c> row when the master's lot differs from the newest one). It has no method that reads a
/// contract for a trading decision.
/// </summary>
public interface IOptionContractStore
{
    /// <summary>The ids of the given canonical symbols that are an index or a share in the master list; a symbol not found is absent from the answer.</summary>
    ValueTask<IReadOnlyDictionary<string, Guid>> ResolveUnderlyingsAsync(IReadOnlyCollection<string> canonicalSymbols, CancellationToken cancellationToken);

    /// <summary>One transaction: insert the new contracts, advance <c>last_seen_on</c> of the others to <paramref name="today"/>, write the missing seeds and the lot changes.</summary>
    ValueTask<OptionContractsApplied> ApplyAsync(
        IReadOnlyList<OptionContractCandidate> contracts,
        IReadOnlyList<LotObservation> lots,
        IReadOnlyList<LotSizeSeed> seeds,
        DateOnly today,
        CancellationToken cancellationToken);
}
