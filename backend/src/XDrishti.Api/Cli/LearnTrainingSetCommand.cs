using System.Globalization;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;

namespace XDrishti.Api.Cli;

/// <summary><c>xd learn training-set</c> (spec P6-04, ADR 0066): builds and keeps the training set of stored runs by calling the use case. No decision lives here. Exit codes follow the other <c>xd</c> commands.</summary>
internal static class LearnTrainingSetCommand
{
    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        var (options, errors) = LearnTrainingSetOptions.Parse(args);
        if (options is null)
        {
            foreach (var error in errors)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(error));
            }

            await console.Error.WriteAsync(LearnTrainingSetOptions.Usage);
            return XdCommand.UsageError;
        }

        try
        {
            await using var scope = services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<BuildTrainingSetCommand, TrainingSetOutcome>>()
                .HandleAsync(new BuildTrainingSetCommand(options.Sources, options.Options, options.Required, "cli:xd"), cancellationToken);
            if (result.IsFailure)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(result.Error.Code + ": " + result.Error.Message));
                return XdCommand.Refused;
            }

            var o = result.Value;
            await console.Out.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"training set {o.Id[..12]} ({(o.AlreadyStored ? "already stored" : "stored")}): {o.Rows} rows, {o.Positives} positive, {o.Negatives} negative; left out: {o.Unresolved} unresolved, {o.NoTarget} without target, {o.DroppedNoFeatures} without features, {o.DroppedUnknown} with a required feature unknown"));
            foreach (var (name, count) in o.UnknownByFeature.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal))
            {
                await console.Out.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"  unknown {name}: {count}"));
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
