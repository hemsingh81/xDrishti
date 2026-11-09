using System.Globalization;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Market;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Platform.Services;

/// <summary>
/// The notices of the services for the top-bar bell, derived on read like the data and account notices (ADR 0018 §7): <c>service_failed</c> for the latest run of each service that has <i>notify on failure</i>
/// when it failed and its sequence is over: the last attempt, a timeout, a run that was interrupted, a service paused meanwhile or a retry that is long overdue (<see cref="ServiceRetryRules"/>). The next run of the service — a success, but also a skipped or new running one — replaces the failed run
/// as the latest and so clears the notice. Id <c>service:failed:{serviceId}:{yyyy-MM-dd}</c> (the India date the failed run started): one notice per service per day, so a service that fails every few minutes during an outage raises the bell once a day, not at every run (the bell keeps what is read by id); the text and the time are those of the latest failure.
/// </summary>
public sealed record GetServiceNoticesQuery : IQuery<ServiceNotices>;

internal sealed class GetServiceNoticesHandler(ServiceRegistry registry, ServiceSettingsReader settings, IServiceRunStore runs, IRecoveryLedger recovery, RecoverySettings recoveryOptions, TimeProvider clock) : IQueryHandler<GetServiceNoticesQuery, ServiceNotices>
{
    public const string Failed = "service_failed";

    public const string RecoveryFailed = "recovery_failed";

    public const string RecoveryWaiting = "recovery_waiting";

    private const int MaxReasonLength = 160;

    private const int RecoveryNoticeDays = 7;

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
                notices.Add(new ServiceNotice($"service:failed:{service.Id}:{IndiaTime.Today(run.StartedAt).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}", Failed, "error", $"{service.Definition.Name} failed on {day}: {reason}", service.Id, run.FinishedAt ?? run.StartedAt));
            }
        }

        // a step of the recovery pass that used up its attempts: one notice per service per day (the day of the pass that last tried it)
        foreach (var exhausted in await recovery.ListExhaustedAsync(recoveryOptions.MaxAttempts, now.AddDays(-RecoveryNoticeDays), cancellationToken))
        {
            if (registry.Find(exhausted.ServiceId) is not { } service)
            {
                continue;
            }

            var day = IndiaTime.Today(exhausted.At).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var id = $"recovery:failed:{exhausted.ServiceId}:{day}";
            if (!notices.Any(n => n.Id == id))
            {
                notices.Add(new ServiceNotice(id, RecoveryFailed, "error", $"{service.Definition.Name} could not be made up for {exhausted.Occurrence}: {(string.IsNullOrWhiteSpace(exhausted.Reason) ? "see the History of the service" : Short(exhausted.Reason))}", exhausted.ServiceId, exhausted.At));
            }
        }

        // a step that has waited for days (no connected account, a source that is down): one warning per service per day, though the wait costs no attempt
        var waitingBefore = IndiaTime.Today(now).AddDays(-recoveryOptions.WaitingNoticeDays);
        foreach (var waiting in await recovery.ListWaitingAsync(waitingBefore, cancellationToken))
        {
            if (registry.Find(waiting.ServiceId) is not { } service)
            {
                continue;
            }

            var id = $"recovery:waiting:{waiting.ServiceId}:{IndiaTime.Today(now).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
            if (!notices.Any(n => n.Id == id))
            {
                notices.Add(new ServiceNotice(id, RecoveryWaiting, "warning", $"{service.Definition.Name} is still waiting to be made up for {waiting.Occurrence}: {(string.IsNullOrWhiteSpace(waiting.Reason) ? "see the History of the service" : Short(waiting.Reason))}", waiting.ServiceId, waiting.At));
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
