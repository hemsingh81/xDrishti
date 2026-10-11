using XDrishti.Domain.Auth;

namespace XDrishti.Application.Abstractions.Auth;

/// <summary>A session together with its owner, loaded by one indexed query.</summary>
public sealed record SessionWithOwner(AuthSession Session, OwnerAccount Owner);

/// <summary>Persistence port for login sessions. Changes are saved by <see cref="IAuthUnitOfWork"/>.</summary>
public interface ISessionStore
{
    ValueTask<SessionWithOwner?> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken);

    ValueTask<SessionWithOwner?> FindByIdAsync(Guid sessionId, CancellationToken cancellationToken);

    /// <summary>Sessions of the owner that have not been revoked (they may still have expired by time).</summary>
    ValueTask<IReadOnlyList<AuthSession>> ListNotRevokedAsync(Guid ownerId, CancellationToken cancellationToken);

    void Add(AuthSession session);
}
