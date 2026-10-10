using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Auth;
using XDrishti.Domain.Auth;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Auth;

internal sealed class AuditLog(XDrishtiDbContext db) : IAuditLog
{
    public void Record(AuditEvent auditEvent) => db.AuditEvents.Add(auditEvent);

    public async ValueTask<IReadOnlyList<AuditEvent>> ListLatestAsync(int take, CancellationToken cancellationToken) =>
        await db.AuditEvents.AsNoTracking().OrderByDescending(x => x.At).ThenByDescending(x => x.Id).Take(take).ToListAsync(cancellationToken);
}
