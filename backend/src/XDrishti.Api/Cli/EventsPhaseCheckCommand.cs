using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.LearningPhase;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Cli;

/// <summary><c>xd events phase-check</c> (spec P15-12): prints the P15 phase check from stored verdicts by calling the use case. Read only; no decision lives here. Exit 0 whatever the verdict says.</summary>
internal static class EventsPhaseCheckCommand
{
    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        var (options, errors) = EventsPhaseCheckOptions.Parse(args);
        if (options is null)
        {
            foreach (var error in errors)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(error));
            }

            await console.Error.WriteAsync(EventsPhaseCheckOptions.Usage);
            return XdCommand.UsageError;
        }

        try
        {
            await using var scope = services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IQueryHandler<EventsPhaseCheckQuery, Result<EventsPhaseCheckView>>>()
                .HandleAsync(new EventsPhaseCheckQuery(options.Cut), cancellationToken);
            if (result.IsFailure)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(result.Error.Code + ": " + result.Error.Message));
                return XdCommand.Refused;
            }

            await console.Out.WriteAsync(options.Json ? result.Value.Json : result.Value.Text);
            return XdCommand.Done;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await console.Error.WriteLineAsync("cancelled");
            return XdCommand.Cancelled;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            await console.Error.WriteLineAsync("failed: " + SimulateCommand.Describe(failure));
            return XdCommand.Failed;
        }
    }
}
