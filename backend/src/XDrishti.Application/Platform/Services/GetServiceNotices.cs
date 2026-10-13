using System.Globalization;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Market;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Platform.Services;

/// <summary>
/// The notices of the services for the top-bar bell, derived on read like the data and account notices (ADR 0018 §7): <c>service_failed</c> for the latest run of each service that has <i>notify on failure</i>
/// when it failed and its sequence is over: the last attempt, a timeout, a run that was interrupted, a service paused meanwhile or a retry that is long overdue (<see cref="ServiceRetryRules"/>). The next run of the service — a success, but also a skipped or new running one — replaces the failed run
/// as the latest and so clears the notice. Id <c>service:failed:{runId}</c>.
/// </summary>
public sealed record GetServiceNoticesQuery : IQuery<ServiceNotices>;

internal sealed class GetServiceNoticesHandler(ServiceRegistry registry, ServiceSettingsReader settings, IServiceRunStore runs, TimeProvider clock) : IQueryHandler<GetServiceNoticesQuery, ServiceNotices>
{
    public const string Failed = "service_failed";

    private const int MaxReasonLength = 160;

    public async ValueTask<ServiceNotices> HandleAsync(GetServiceNoticesQuery query, CancellationToken cancellationToken)
    {
        var all = await settings.ReadAllAsync(cancellationToken);
        var latest = await runs.GetLatestAsync(registry.Ids, cancellationToken);
        var now = clock.GetUtcNow();
        var notices = new List<ServiceNotice>();
        foreach (var service in all.Where(s => s.Settings.NotifyOnFailure))
        {
            if (latest.TryGetValue(service.Id, out var run) && run is { Status: ServiceRunStatus.Failed } && ServiceRetryRules.IsOver(run, service.Settings.Retries, service.Settings.Enabled, now))
            {
                var day = IndiaTime.Today(run.StartedAt).ToString("d MMM", CultureInfo.InvariantCulture);
                var reason = string.IsNullOrWhiteSpace(run.Message) ? "see the History of the service" : Short(run.Message);
                notices.Add(new ServiceNotice($"service:failed:{run.Id}", Failed, "error", $"{service.Definition.Name} failed on {day}: {reason}", service.Id, run.FinishedAt ?? run.StartedAt));
            }
        }

        return new ServiceNotices(notices);
    }

    private static string Short(string text)
    {
        var line = text.ReplaceLineEndings(" ").Trim();
        return line.Length <= MaxReasonLength ? line : line[..MaxReasonLength] + "…";
    }
}
