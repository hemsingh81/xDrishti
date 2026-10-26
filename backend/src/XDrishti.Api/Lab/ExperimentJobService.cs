using XDrishti.Application.Learning.Jobs;

namespace XDrishti.Api.Lab;

/// <summary>Hosts the replay job dispatcher in <c>xd-api</c> (spec P8-18 Part B, ADR 0129): all logic is in <see cref="ExperimentJobDispatcher"/>. Off when the configuration says <c>Experiments:RunWorker=false</c> (the integration tests drive the dispatcher themselves).</summary>
internal sealed class ExperimentJobService(ExperimentJobDispatcher dispatcher, ExperimentJobSettings settings) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        settings.RunWorker ? dispatcher.RunAsync(stoppingToken) : Task.CompletedTask;
}
