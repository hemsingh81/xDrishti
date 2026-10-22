using XDrishti.Domain.Common;

namespace XDrishti.Application.Abstractions.Research;

/// <summary>
/// The stored strategies (spec P3-02d, ADR 0056): families and their immutable versions in the <c>lab</c> schema, insert-only for the runtime role. The store keeps text and hashes and judges neither: the caller has read and compiled the text
/// (the engine does, behind <see cref="ISimulationEngine"/>).
/// </summary>
public interface IStrategyStore
{
    /// <summary>All families, by id.</summary>
    ValueTask<IReadOnlyList<StoredStrategy>> ListAsync(CancellationToken cancellationToken);

    /// <summary>One family, or null.</summary>
    ValueTask<StoredStrategy?> GetAsync(string id, CancellationToken cancellationToken);

    /// <summary>A version of a family (the latest when <paramref name="version"/> is null), or null.</summary>
    ValueTask<StoredVersion?> GetVersionAsync(string id, int? version, CancellationToken cancellationToken);

    /// <summary>A version by its content hash, or null.</summary>
    ValueTask<StoredVersion?> GetByHashAsync(string hash, CancellationToken cancellationToken);

    /// <summary>
    /// Appends a version in one transaction. The same hash again is the same row (<see cref="AppendResult.Created"/> false, nothing written, whatever the expected version). Otherwise the family is created if new and the version gets
    /// <c>max + 1</c>. Concurrent appends are safe: a lost race on the version is retried, a lost race on the hash re-reads.
    /// </summary>
    ValueTask<Result<AppendResult>> AppendAsync(NewVersion version, CancellationToken cancellationToken);
}
