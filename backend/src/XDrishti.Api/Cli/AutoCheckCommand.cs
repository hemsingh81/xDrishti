using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auto;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Cli;

/// <summary><c>xd auto check</c> (spec AU-19, ADR 0253): prints the AU programme check from stored records by calling the use case. Read only; no decision lives here. Exit 0 whatever the verdict says, 2 for a wrong line, 3 failure, 130 cancelled.</summary>
internal static class AutoCheckCommand
{
    public const string Usage = """
        usage:
          xd auto check
            judges the AU exit items from stored records: a multi-day recovery in the ledger, the removal ADR, the Essential settings count, a finished simulation job and its evidence fence,
            a night of suggestions with Auto ON; each MET / NOT MET / NOT EVALUABLE, limits printed; an unreadable or empty fact is never MET
            nothing is written and no order is placed
        exit codes: 0 done (whatever the verdict says), 2 wrong command line, 3 failure, 130 cancelled

        """;

    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        if (args.Count > 0)
        {
            await console.Error.WriteLineAsync($"unknown argument '{SimulateOptions.Show(args[0])}'");
            await console.Error.WriteAsync(Usage);
            return XdCommand.UsageError;
        }

        try
        {
            await using var scope = services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IQueryHandler<EvaluateAutoProgrammeCheckQuery, Result<AutoProgrammeCheckView>>>()
                .HandleAsync(new EvaluateAutoProgrammeCheckQuery(), cancellationToken);
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
