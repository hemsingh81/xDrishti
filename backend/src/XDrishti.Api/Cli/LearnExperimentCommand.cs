using System.Globalization;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;

namespace XDrishti.Api.Cli;

/// <summary><c>xd learn experiment</c> (spec P6-11, ADR 0072): judges a knob change between two stored runs and records it by calling the use case. No decision lives here. Exit codes follow the other <c>xd</c> commands.</summary>
internal static class LearnExperimentCommand
{
    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        var (options, errors) = LearnExperimentOptions.Parse(args);
        if (options is null)
        {
            foreach (var error in errors)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(error));
            }

            await console.Error.WriteAsync(LearnExperimentOptions.Usage);
            return XdCommand.UsageError;
        }

        try
        {
            await using var scope = services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<RunExperimentCommand, RunExperimentOutcome>>()
                .HandleAsync(new RunExperimentCommand(options.BaselineRunKey, options.CandidateRunKey, options.Knob, options.Options, options.HoldOutMonths, "cli:xd"), cancellationToken);
            if (result.IsFailure)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(result.Error.Code + ": " + result.Error.Message));
                return XdCommand.Refused;
            }

            var o = result.Value;
            await console.Out.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"experiment {o.Id} ({(o.AlreadyRecorded ? "already recorded" : "recorded")}): {o.Outcome}, {o.Trials} trials in 30 days, compared in {o.CompareMs} ms"));
            await console.Out.WriteLineAsync(o.ReportText);
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
