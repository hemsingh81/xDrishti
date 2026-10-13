using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Platform.Services;

/// <summary>Reads the services the way the screen shows them: the settings now in force, the latest run of each, the findings over the whole set and the next start by the trading calendar.</summary>
internal sealed class ServiceViewReader(ServiceRegistry registry, ServiceSettingsReader settings, IServiceRunStore runs, ITradingCalendar calendar, AccountSyncEntry accountSync, TimeProvider clock)
{
    public async ValueTask<IReadOnlyList<ServiceView>> ReadAllAsync(CancellationToken cancellationToken)
    {
        var all = await settings.ReadAllAsync(cancellationToken);
        var latest = await runs.GetLatestAsync(registry.Ids, cancellationToken);
        var running = await runs.GetRunningServicesAsync(cancellationToken);
        var tradingCalendar = await calendar.GetAsync(cancellationToken);
        var issues = ScheduleRules.Evaluate(ServiceMapper.Subjects(all)).ToList();
        issues.InsertRange(0, all.Where(s => s.StoredInvalid).Select(s => ScheduleIssue.Error(s.Id, "schedule", ScheduleIssueCodes.StoredInvalid, "The stored schedule could not be read. The service runs on its default schedule until a new one is saved.")));
        var now = clock.GetUtcNow();
        var views = all.Select(s => ServiceMapper.ToView(s, latest.GetValueOrDefault(s.Id), running.Contains(s.Id), issues, tradingCalendar, now, registry.NameOf)).ToList();
        views.Add(await accountSync.ReadAsync(cancellationToken));
        return views;
    }

    /// <summary>One service by id (a registered one or the read-only account sync); null when there is none.</summary>
    public async ValueTask<ServiceView?> ReadAsync(string serviceId, CancellationToken cancellationToken)
    {
        if (serviceId != ServiceIds.AccountSync && registry.Find(serviceId) is null)
        {
            return null;
        }

        return (await ReadAllAsync(cancellationToken)).FirstOrDefault(v => v.Id == serviceId);
    }
}
