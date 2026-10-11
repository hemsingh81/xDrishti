using XDrishti.Domain.Auth;

namespace XDrishti.Application.Abstractions.Auth;

/// <summary>Persistence port for the owner account (there is exactly one). Changes are saved by <see cref="IAuthUnitOfWork"/>.</summary>
public interface IOwnerAccountStore
{
    ValueTask<OwnerAccount?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken);

    /// <summary>The owner, or null when none has been provisioned yet.</summary>
    ValueTask<OwnerAccount?> FindSoleOwnerAsync(CancellationToken cancellationToken);

    void Add(OwnerAccount owner);
}
