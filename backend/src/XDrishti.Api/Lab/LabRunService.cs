using XDrishti.Application.Lab;

namespace XDrishti.Api.Lab;

/// <summary>Hosts the Lab run dispatcher in <c>xd-api</c> (ADR 0058): all logic is in <see cref="LabRunDispatcher"/>. Off when the configuration says <c>StrategyLab:RunWorker=false</c> (the integration tests drive the dispatcher themselves).</summary>
internal sealed class LabRunService(LabRunDispatcher dispatcher, IConfiguration configuration) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        configuration.GetValue("StrategyLab:RunWorker", true) ? dispatcher.RunAsync(stoppingToken) : Task.CompletedTask;
}
