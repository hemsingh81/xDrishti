using System.Globalization;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;

namespace XDrishti.Api.Cli;

/// <summary><c>xd learn features</c> (spec P6-04, ADR 0066): extracts and keeps the decision-time features of stored runs by calling the use case. No decision lives here. Exit codes follow the other <c>xd</c> commands.</summary>
internal static class LearnFeaturesCommand
{
    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        var (options, errors) = LearnFeaturesOptions.Parse(args);
        if (options is null)
        {
            foreach (var error in errors)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(error));
            }

            await console.Error.WriteAsync(LearnFeaturesOptions.Usage);
            return XdCommand.UsageError;
        }

        try
        {
            foreach (var runKey in options.RunKeys)
            {
                await using var scope = services.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<ExtractTradeFeaturesCommand, TradeFeaturesOutcome>>()
                    .HandleAsync(new ExtractTradeFeaturesCommand(runKey), cancellationToken);
                if (result.IsFailure)
                {
                    await console.Error.WriteLineAsync(SimulateOptions.Show(result.Error.Code + ": " + result.Error.Message));
                    return XdCommand.Refused;
                }

                var o = result.Value;
                await console.Out.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"features of run {runKey[..Math.Min(12, runKey.Length)]}: {o.Trades} trades, {o.Stored} new rows, {o.NoBars} without bars"));
            }

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
