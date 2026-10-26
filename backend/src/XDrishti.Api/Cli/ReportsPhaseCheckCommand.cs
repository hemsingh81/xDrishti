using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Cli;

/// <summary><c>xd reports phase-check</c> (spec P8-17, ADR 0126): prints the P8 phase check as text. Read only; exit 0 whatever the verdicts say.</summary>
internal static class ReportsPhaseCheckCommand
{
    public const string Usage = """
        usage:
          xd reports phase-check
            judges the P8 exit from the stored runs: criteria 1 to 10 of P8-00 each MET, NOT MET or NOT EVALUABLE with its detail, the answer the What-works view gives on the default set, the cold and warm time of each report read and the honest limits (always printed)
            a criterion that could not be read is never MET; the interaction test, the owner's stopwatch note, the bundle and the browser pass are read by scripts/p8-phase-check.sh
            nothing is written, no job is run and no setting is changed; this host has no route table, so criterion 5 reads NOT EVALUABLE here (the endpoint GET /api/reports/phase-check judges it)
        exit codes: 0 done (whatever the verdicts), 2 wrong command line, 3 failure, 130 cancelled

        """;

    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        if (args.Count > 0)
        {
            await console.Error.WriteAsync(Usage);
            return XdCommand.UsageError;
        }

        try
        {
            await using var scope = services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IQueryHandler<EvaluateReportsPhaseCheckQuery, Result<ReportsPhaseCheckView>>>()
                .HandleAsync(new EvaluateReportsPhaseCheckQuery(), cancellationToken);
            if (result.IsFailure)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(result.Error.Code + ": " + result.Error.Message));
                return XdCommand.Failed;
            }

            await console.Out.WriteAsync(SimulateOptions.Show(result.Value.Text));
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
