using System.Globalization;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Planning;

namespace XDrishti.Api.Cli;

/// <summary><c>xd plan run</c> (spec P7-08, ADR 0086): runs the nightly pipeline once by calling the use case, as a manual run. No decision lives here. Exit codes follow the other <c>xd</c> commands; a run that failed closed exits 1 with the gate and reason.</summary>
internal static class PlanRunCommand
{
    private const string Actor = "cli:xd";

    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        var (options, errors) = PlanRunOptions.Parse(args);
        if (options is null)
        {
            foreach (var error in errors)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(error));
            }

            await console.Error.WriteAsync(PlanRunOptions.Usage);
            return XdCommand.UsageError;
        }

        try
        {
            await using var scope = services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<RunNightlyPlanCommand, NightlyRunView>>()
                .HandleAsync(new RunNightlyPlanCommand(options.Date, options.DryRun, true, Actor), cancellationToken);
            if (result.IsFailure)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(result.Error.Code + ": " + result.Error.Message));
                return XdCommand.Refused;
            }

            var run = result.Value;
            await console.Out.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"session {run.Session:yyyy-MM-dd}, plan for {run.ForDate:yyyy-MM-dd}: {run.Outcome}{(run.DryRun ? " (dry run, nothing written)" : string.Empty)}{(run.Repeated ? " (already done, nothing changed)" : string.Empty)}, attempt {run.Attempt}"));
            foreach (var gate in run.Gates)
            {
                await console.Out.WriteLineAsync(SimulateOptions.Show($"  {gate.Status,-12} {gate.Gate}: {gate.Reason}"));
            }

            await console.Out.WriteLineAsync(SimulateOptions.Show(run.Reason));
            return run.Outcome == "failed_closed" ? XdCommand.Refused : XdCommand.Done;
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
