using XDrishti.Application.Platform.Services;

namespace XDrishti.Api.Platform;

/// <summary>Hosts the one scheduler of the automatic jobs (ADR 0018); the schedules, the catch-up, the retries and the work are in <see cref="ServiceScheduler"/>.</summary>
internal sealed class ServiceSchedulerHost(ServiceScheduler scheduler) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => scheduler.RunAsync(stoppingToken);
}
