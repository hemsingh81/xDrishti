using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Execution;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Cli;

/// <summary><c>xd exec phase-check</c> (spec P12-12, ADR 0169): prints the P12 phase check from stored records by calling the use case. Read only; no decision lives here. Exit codes follow the other <c>xd</c> commands.</summary>
internal static class ExecPhaseCheckCommand
{
    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        var (options, errors) = ExecPhaseCheckOptions.Parse(args);
        if (options is null)
        {
            foreach (var error in errors)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(error));
            }

            await console.Error.WriteAsync(ExecPhaseCheckOptions.Usage);
            return XdCommand.UsageError;
        }

        try
        {
            await using var scope = services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IQueryHandler<EvaluateExecutionPhaseCheckQuery, Result<ExecutionPhaseCheckView>>>()
                .HandleAsync(new EvaluateExecutionPhaseCheckQuery(options.Through, options.AccountId), cancellationToken);
            if (result.IsFailure)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(result.Error.Code + ": " + result.Error.Message));
                return XdCommand.Refused;
            }

            await console.Out.WriteAsync(result.Value.Text);
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
