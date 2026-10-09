using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Platform;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Platform;

internal sealed class ServiceHeartbeatStore(XDrishtiDbContext db) : IServiceHeartbeatStore
{
    public async ValueTask<ServiceHeartbeat?> FindAsync(string serviceName, CancellationToken cancellationToken) =>
        await db.ServiceHeartbeats.FindAsync([serviceName], cancellationToken);

    // Read-only list: no change tracking (faster, less memory).
    public async ValueTask<IReadOnlyList<ServiceHeartbeat>> ListAsync(CancellationToken cancellationToken) =>
        await db.ServiceHeartbeats.AsNoTracking().OrderBy(x => x.Id).ToListAsync(cancellationToken);

    public void Add(ServiceHeartbeat heartbeat) => db.ServiceHeartbeats.Add(heartbeat);

    public async ValueTask SaveChangesAsync(CancellationToken cancellationToken) =>
        await db.SaveChangesAsync(cancellationToken);
}
