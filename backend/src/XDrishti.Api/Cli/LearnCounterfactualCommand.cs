using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Api.Cli;

/// <summary><c>xd learn counterfactual</c> (spec P6-12, ADR 0077): prints the frozen-model counterfactual by calling the use case. No decision lives here. Exit codes follow the other <c>xd</c> commands.</summary>
internal static class LearnCounterfactualCommand
{
    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        var (options, errors) = LearnCounterfactualOptions.Parse(args);
        if (options is null)
        {
            foreach (var error in errors)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(error));
            }

            await console.Error.WriteAsync(LearnCounterfactualOptions.Usage);
            return XdCommand.UsageError;
        }

        try
        {
            await using var scope = services.CreateAsyncScope();
            var query = new EvaluateCounterfactualQuery(options.Sources, new CellStatsOptions(), null, new ValidationOptions(), options.Counterfactual, options.HoldOutMonths);
            if (options.Store)
            {
                var stored = await scope.ServiceProvider.GetRequiredService<ICommandHandler<StoreCounterfactualCommand, StoredCounterfactual>>().HandleAsync(new StoreCounterfactualCommand(query, "cli:xd"), cancellationToken);
                if (stored.IsFailure)
                {
                    await console.Error.WriteLineAsync(SimulateOptions.Show(stored.Error.Code + ": " + stored.Error.Message));
                    return XdCommand.Refused;
                }

                await console.Out.WriteAsync(stored.Value.Text);
                await console.Out.WriteLineAsync(stored.Value.Created ? $"stored as #{stored.Value.Id}" : $"already stored as #{stored.Value.Id} (same inputs)");
                return XdCommand.Done;
            }

            var result = await scope.ServiceProvider.GetRequiredService<IQueryHandler<EvaluateCounterfactualQuery, Result<CounterfactualReport>>>()
                .HandleAsync(query, cancellationToken);
            if (result.IsFailure)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(result.Error.Code + ": " + result.Error.Message));
                return XdCommand.Refused;
            }

            await console.Out.WriteAsync(result.Value.ToText());
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
