using XDrishti.Domain.Learning;

namespace XDrishti.Application.Abstractions.Learning;

/// <summary>Where registered models and their designations are kept (P6-07, ADR 0070): <c>research.model_versions</c> and <c>research.model_designations</c> behind a port. Both are insert-only.</summary>
public interface IModelRegistry
{
    /// <summary>Registers the model with the role challenger; nothing is written when the version key is stored already.</summary>
    ValueTask<RegisteredModel> RegisterAsync(ModelToRegister model, CancellationToken cancellationToken);

    /// <summary>How many distinct models (by file hash) other than <paramref name="sha256"/> are registered for the feature version: the trials already looked at.</summary>
    ValueTask<int> CountOtherModelsAsync(string featureVersion, string sha256, CancellationToken cancellationToken);

    /// <summary>One model, or null.</summary>
    ValueTask<StoredModel?> FindAsync(long id, CancellationToken cancellationToken);

    /// <summary>The newest models first, optionally only those whose current role is <paramref name="role"/>. The report, parameters and arrays are left empty.</summary>
    ValueTask<IReadOnlyList<StoredModel>> ListAsync(ModelRole? role, int limit, CancellationToken cancellationToken);

    /// <summary>The designations of a model, oldest first.</summary>
    ValueTask<IReadOnlyList<StoredDesignation>> DesignationsAsync(long id, CancellationToken cancellationToken);

    /// <summary>
    /// Appends a designation in one transaction, after checking again under the lock that the model does not already have the role (nothing is written then: false). Naming a champion also appends <see cref="ModelRole.Retired"/> for the model that was champion.
    /// <paramref name="evidence"/> are extra evidence ids for the changelog row of the designation (for example the rollback proposal and the switch row of an automatic act); the model's own subject is always the first.
    /// <paramref name="expectedCurrent"/>, when given, is the role the model must have <b>now</b>, checked under the same lock as the rest: any other role writes nothing (false), so a demotion decided on a stale read (the model was retired in between) can never move a retired model into the challenger pool.
    /// </summary>
    ValueTask<bool> DesignateAsync(long id, ModelRole role, string reason, string actor, DateTimeOffset at, IReadOnlyList<string> evidence, ModelRole? expectedCurrent, CancellationToken cancellationToken);
}
