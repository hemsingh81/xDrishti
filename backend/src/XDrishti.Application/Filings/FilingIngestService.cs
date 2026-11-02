using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Filings;

/// <summary>
/// The filing ingest as a service (<see cref="Id"/>, P15-02, ADR 0180): every trading day at 17:05 India time, and on "Run now", it stores the files the owner dropped into the filings inbox and loads the board-meeting and shareholding csv files (P15-04). It starts <b>paused</b>: with the service paused nothing scans the folder. It touches only
/// local files and its own tables (no network, no broker, no order), so "Run now" is allowed. Retries 1, no catch-up. It runs in the scheduler of <c>xd-api</c>, the only host that has one.
/// </summary>
public sealed class FilingIngestService(IServiceScopeFactory scopes, ILogger<FilingIngestService> logger) : IScheduledService
{
    /// <summary>The id of the service (part of the API and of the stored row: never rename it).</summary>
    public const string Id = "filings-ingest";

    public static readonly ServiceDefinition Definition = new(
        Id,
        "Filings ingest",
        "Reads the files you dropped into the filings folder (with their sidecar that carries the exchange broadcast time) and stores each one once, unchanged, with that time. A filing without a usable time is stored and counted, never dated and never used by a backtest. Reads local files only: no network, no broker, no order.",
        "xd-api",
        [ScheduleType.Daily, ScheduleType.Weekly, ScheduleType.After],
        new ServiceSettings(false, Schedule.Daily(new TimeOnly(17, 5)), 1, 30, false, true, new Dictionary<string, System.Text.Json.JsonElement>()),
        [],
        new ServiceConstraints { CatchUpReach = CatchUpReach.SameDay });

    ServiceDefinition IScheduledService.Definition => Definition;

    public ServiceSettings? FirstStartSettings() => null;

    public async ValueTask<ServiceOutcome> RunAsync(ServiceRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await using var scope = scopes.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<IngestFilingsCommand, FilingIngestSummary>>();
        var result = await handler.HandleAsync(new IngestFilingsCommand(context.Trigger == ServiceRunTrigger.Manual), cancellationToken);
        if (result.IsFailure)
        {
            return ServiceOutcome.Failed(result.Error.Message);
        }

        var summary = result.Value;
        var calendar = await RunLoaderAsync<LoadCalendarCommand>(scope.ServiceProvider, LoadCalendarHandler.Kind, new LoadCalendarCommand(), cancellationToken);
        var shareholding = await RunLoaderAsync<LoadShareholdingCommand>(scope.ServiceProvider, LoadShareholdingHandler.Kind, new LoadShareholdingCommand(), cancellationToken);
        var text = string.Join("; ", new[] { summary.Text, calendar.Text, shareholding.Text });
        var loaderFailures = (calendar.Failed ? 1 : 0) + (shareholding.Failed ? 1 : 0);
        return summary.Stopped is not null || summary.Failures > 0 || summary.Problems.Count > 0 || loaderFailures > 0 ? ServiceOutcome.Failed(text) : ServiceOutcome.Succeeded(text);
    }

    // A loader that throws or fails is logged by exception type only, marks the run failed and never stops the other loader.
    private async ValueTask<(string Text, bool Failed)> RunLoaderAsync<TCommand>(IServiceProvider services, string kind, TCommand command, CancellationToken cancellationToken)
        where TCommand : ICommand
    {
        try
        {
            var result = await services.GetRequiredService<ICommandHandler<TCommand, RowLoadSummary>>().HandleAsync(command, cancellationToken);
            return result.IsSuccess ? (result.Value.Text, result.Value.Failures > 0) : ($"{kind}: loader failed", true);
        }
#pragma warning disable CA1031 // a failing loader must not stop the other one or any other service: it is logged by type and the run is marked failed
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            RowLoadLog.Failed(logger, kind, ex.GetType().Name);
            return ($"{kind}: loader failed ({ex.GetType().Name})", true);
        }
    }
}
