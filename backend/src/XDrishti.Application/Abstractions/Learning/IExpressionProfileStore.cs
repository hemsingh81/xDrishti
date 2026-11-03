using XDrishti.Domain.Learning;

namespace XDrishti.Application.Abstractions.Learning;

/// <summary>
/// Where expression profiles are kept (P14-06, ADR 0193): <c>lab.expression_profiles</c> behind a port. Insert-only, content-addressed by <see cref="ExpressionProfile.Hash"/>: a stored profile is never updated.
/// </summary>
public interface IExpressionProfileStore
{
    /// <summary>Stores the profile; false (nothing written) when its hash is stored already.</summary>
    ValueTask<bool> AppendAsync(ExpressionProfile profile, CancellationToken cancellationToken);

    /// <summary>The profile with this hash, or null. A stored row that does not hash to its key is an error, not a profile.</summary>
    ValueTask<StoredExpressionProfile?> GetAsync(string profileHash, CancellationToken cancellationToken);

    /// <summary>Every stored profile, oldest first.</summary>
    ValueTask<IReadOnlyList<StoredExpressionProfile>> ListAsync(CancellationToken cancellationToken);
}
