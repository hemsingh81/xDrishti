using Microsoft.EntityFrameworkCore;
using Npgsql;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Platform.Services;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Platform;

internal sealed class ServiceRunStore(XDrishtiDbContext db) : IServiceRunStore
{
    private const string StaleMessage = "Interrupted: the run did not end (the application stopped or crashed while it ran).";

    public async ValueTask<bool> TryStartAsync(ServiceRun run, TimeSpan staleAfter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        var cutoff = run.StartedAt - staleAfter;
        var stale = ServiceRunStatus.Running;
        var failed = ServiceRunStatus.Failed;
        await db.ServiceRuns
            .Where(x => x.ServiceId == run.ServiceId && x.Status == stale && x.StartedAt < cutoff)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, failed).SetProperty(x => x.FinishedAt, run.StartedAt).SetProperty(x => x.Message, StaleMessage), cancellationToken);

        db.ServiceRuns.Add(run);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(run).State = EntityState.Detached; // the one-running-run index caught it
            return false;
        }
    }

    public async ValueTask RecordAsync(ServiceRun ended, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ended);
        db.ServiceRuns.Add(ended);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async ValueTask CompleteAsync(Guid runId, ServiceRunStatus status, string? message, DateTimeOffset finishedAt, CancellationToken cancellationToken)
    {
        var running = ServiceRunStatus.Running;
        await db.ServiceRuns
            .Where(x => x.Id == runId && x.Status == running)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, status).SetProperty(x => x.FinishedAt, finishedAt).SetProperty(x => x.Message, message), cancellationToken);
    }

    public async ValueTask LinkDataJobAsync(Guid runId, Guid dataJobId, CancellationToken cancellationToken) =>
        await db.ServiceRuns.Where(x => x.Id == runId).ExecuteUpdateAsync(s => s.SetProperty(x => x.DataJobId, dataJobId), cancellationToken);

    public async ValueTask<IReadOnlyDictionary<string, ServiceRun>> GetLatestAsync(IReadOnlyCollection<string> serviceIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serviceIds);
        if (serviceIds.Count == 0)
        {
            return new Dictionary<string, ServiceRun>();
        }

        // one indexed read per service (ix_service_runs_service_id_started_at), put together: never a scan of the whole history. The text holds only numbered parameters.
        var ids = serviceIds.Distinct(StringComparer.Ordinal).ToArray();
        var sql = string.Join(
            " UNION ALL ",
            ids.Select((_, i) => $"(SELECT * FROM platform.service_runs WHERE service_id = {{{i}}} ORDER BY started_at DESC, id DESC LIMIT 1)"));
        var latest = await db.ServiceRuns.FromSqlRaw(sql, [.. ids]).AsNoTracking().ToListAsync(cancellationToken);
        return latest.ToDictionary(r => r.ServiceId, StringComparer.Ordinal);
    }

    public async ValueTask<IReadOnlySet<string>> GetRunningServicesAsync(CancellationToken cancellationToken)
    {
        var running = ServiceRunStatus.Running;
        return (await db.ServiceRuns.AsNoTracking().Where(x => x.Status == running).Select(x => x.ServiceId).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
    }

    public async ValueTask<IReadOnlyList<ServiceRun>> ListAsync(string serviceId, int skip, int take, CancellationToken cancellationToken) =>
        await db.ServiceRuns.AsNoTracking().Where(x => x.ServiceId == serviceId).OrderByDescending(x => x.StartedAt).ThenByDescending(x => x.Id).Skip(skip).Take(take).ToListAsync(cancellationToken);

    public async ValueTask<int> CountAsync(string serviceId, CancellationToken cancellationToken) =>
        await db.ServiceRuns.AsNoTracking().CountAsync(x => x.ServiceId == serviceId, cancellationToken);

    public async ValueTask<int> FailInterruptedAsync(DateTimeOffset processStartedAt, DateTimeOffset now, string message, IReadOnlyCollection<string> exceptServiceIds, CancellationToken cancellationToken)
    {
        var running = ServiceRunStatus.Running;
        var failed = ServiceRunStatus.Failed;
        var except = exceptServiceIds.ToArray();
        return await db.ServiceRuns
            .Where(x => x.Status == running && x.StartedAt < processStartedAt && !except.Contains(x.ServiceId))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, failed).SetProperty(x => x.FinishedAt, now).SetProperty(x => x.Message, message), cancellationToken);
    }
}
