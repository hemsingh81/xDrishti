using XDrishti.Domain.Lab;

namespace XDrishti.Application.Abstractions.Lab;

/// <summary>A stored pre-registration of one strategy version (spec CF-06, D1).</summary>
public sealed record StoredRegistration(string StrategyId, int Version, Registration Registration, string TextHash, DateTimeOffset CreatedAt, string Actor);

/// <summary><c>lab.registrations</c> behind a port (spec CF-06): insert-only for the runtime role, one row per strategy version, frozen once written.</summary>
public interface IRegistrationStore
{
    /// <summary>The registration of the version, or null.</summary>
    ValueTask<StoredRegistration?> GetAsync(string strategyId, int version, CancellationToken cancellationToken);

    /// <summary>Stores it; false (nothing written) when the version already has one.</summary>
    ValueTask<bool> InsertAsync(StoredRegistration registration, CancellationToken cancellationToken);
}
