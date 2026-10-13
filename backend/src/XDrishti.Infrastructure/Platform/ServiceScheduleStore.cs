using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Platform.Services;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Platform;

internal sealed class ServiceScheduleStore(XDrishtiDbContext db) : IServiceScheduleStore
{
    public async ValueTask<IReadOnlyList<ServiceSchedule>> ListAsync(CancellationToken cancellationToken) =>
        await db.ServiceSchedules.AsNoTracking().OrderBy(x => x.Id).ToListAsync(cancellationToken);

    public async ValueTask<ServiceSchedule?> FindAsync(string serviceId, CancellationToken cancellationToken) =>
        await db.ServiceSchedules.FirstOrDefaultAsync(x => x.Id == serviceId, cancellationToken);

    public void Add(ServiceSchedule row) => db.ServiceSchedules.Add(row);
}
