using XDrishti.Domain.Accounts;

namespace XDrishti.Application.Abstractions.Accounts;

/// <summary>Persistence port for shared risk profiles. Changes are saved by <see cref="IAccountUnitOfWork"/>.</summary>
public interface IRiskProfileStore
{
    /// <summary>All profiles ordered by name. Not tracked.</summary>
    ValueTask<IReadOnlyList<RiskProfile>> ListAsync(CancellationToken cancellationToken);

    /// <summary>One profile, tracked; null when it does not exist.</summary>
    ValueTask<RiskProfile?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The name of a profile; null when it does not exist.</summary>
    ValueTask<string?> FindNameAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Loads one profile, tracked, and locks its row until the surrounding transaction ends (<see cref="IAccountUnitOfWork.RunSerializedAsync"/>),
    /// so no account can start referring to it meanwhile; null when it does not exist.
    /// </summary>
    ValueTask<RiskProfile?> FindForDeleteAsync(Guid id, CancellationToken cancellationToken);

    ValueTask<bool> NameExistsAsync(string name, Guid? exceptProfileId, CancellationToken cancellationToken);

    /// <summary>How many active accounts refer to each profile (profiles nobody uses are absent).</summary>
    ValueTask<IReadOnlyDictionary<Guid, int>> CountUsageAsync(CancellationToken cancellationToken);

    void Add(RiskProfile profile);

    void Remove(RiskProfile profile);
}
