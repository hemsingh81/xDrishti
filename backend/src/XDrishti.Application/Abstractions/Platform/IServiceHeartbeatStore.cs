using XDrishti.Domain.Platform;

namespace XDrishti.Application.Abstractions.Platform;

/// <summary>Persistence port for service heartbeats.</summary>
public interface IServiceHeartbeatStore
{
    ValueTask<ServiceHeartbeat?> FindAsync(string serviceName, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<ServiceHeartbeat>> ListAsync(CancellationToken cancellationToken);

    void Add(ServiceHeartbeat heartbeat);

    ValueTask SaveChangesAsync(CancellationToken cancellationToken);
}
