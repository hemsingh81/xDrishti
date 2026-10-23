using System.Globalization;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Api.Cli;

/// <summary><c>xd learn model</c> (spec P6-07, ADR 0070): trains, validates and registers a model from stored runs by calling the use case. No decision lives here. Exit codes follow the other <c>xd</c> commands.</summary>
internal static class LearnModelCommand
{
    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        var (options, errors) = LearnModelOptions.Parse(args);
        if (options is null)
        {
            foreach (var error in errors)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(error));
            }

            await console.Error.WriteAsync(LearnModelOptions.Usage);
            return XdCommand.UsageError;
        }

        try
        {
            await using var scope = services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<TrainModelCommand, TrainModelOutcome>>()
                .HandleAsync(new TrainModelCommand(options.Sources, new CellStatsOptions(), options.Required, options.Validation, options.HoldOutMonths, "cli:xd"), cancellationToken);
            if (result.IsFailure)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(result.Error.Code + ": " + result.Error.Message));
                return XdCommand.Refused;
            }

            var o = result.Value;
            await console.Out.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"model {o.ModelId} ({(o.AlreadyRegistered ? "already registered" : "registered")}) {o.Sha256[..12]}: {o.Trainer}, {o.TrainRows} rows, training set {o.TrainingSetId[..12]}, verdict {o.Verdict}"));
            foreach (var reason in o.Reasons)
            {
                await console.Out.WriteLineAsync("  " + SimulateOptions.Show(reason));
            }

            if (o.Note is not null)
            {
                await console.Out.WriteLineAsync("note: " + SimulateOptions.Show(o.Note));
            }

            await console.Out.WriteLineAsync(o.ReportText);
            await console.Out.WriteLineAsync("Advisory: no plan uses this model until P7; only an Eligible model can be named champion (xd learn designate).");
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
