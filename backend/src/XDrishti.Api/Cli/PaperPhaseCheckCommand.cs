using System.Globalization;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Paper;
using XDrishti.Application.Shadow;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Cli;

/// <summary><c>xd paper phase-check</c> (spec P10-06, ADR 0123): prints the paper phase check as text. Read only; exit 0 whatever the verdicts say.</summary>
internal static class PaperPhaseCheckCommand
{
    public const string Usage = """
        usage:
          xd paper phase-check [--through YYYY-MM-DD] [--epoch ID]
            judges the P10 exit from stored records: duration and coverage, counted trades, expectancy within the backtest band after costs, drawdown within the 90th percentile and capture integrity, each MET, NOT MET or NOT EVALUABLE
            with its facts and blockers, the caveats in the label of a MET, the week lines, every epoch, what is printed and not judged, and the honest limits (always printed)
            --through is rounded down to a trading session (default: the last closed session); only the epoch (default: the newest) is judged and earlier epochs are never pooled
            without --through and --epoch, one extra line follows: the sequential failure test of the paper edge series (xd paper sequential), report only, never a pass
            nothing is written, no replay is run and no setting is changed
        exit codes: 0 done (whatever the verdicts), 1 refused (a bad --through, an unknown epoch), 2 wrong command line, 3 failure, 130 cancelled

        """;

    public static bool Handles(IReadOnlyList<string> args) => args.Count >= 1 && args[0] == "phase-check";

    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        if (!TryParse(args.Skip(1).ToList(), out var through, out var epoch))
        {
            await console.Error.WriteAsync(Usage);
            return XdCommand.UsageError;
        }

        try
        {
            await using var scope = services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IQueryHandler<EvaluatePaperPhaseCheckQuery, Result<PaperPhaseCheckView>>>()
                .HandleAsync(new EvaluatePaperPhaseCheckQuery(through, epoch), cancellationToken);
            if (result.IsFailure)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(result.Error.Code + ": " + result.Error.Message));
                return XdCommand.Refused;
            }

            await console.Out.WriteAsync(SimulateOptions.Show(result.Value.Text));
            await WriteSequentialLineAsync(scope.ServiceProvider, through, epoch, console, cancellationToken);
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

    /// <summary>
    /// The report line of the sequential failure test (spec CF-09), after the check and apart from it: the check's own text and exit code are never touched. Printed for the newest epoch and the latest session only (the test reads the current one as it stands now).
    /// A cancellation goes through; any other failure of the sequential read becomes one note on the error stream and nothing else.
    /// </summary>
    internal static async Task WriteSequentialLineAsync(IServiceProvider services, DateOnly? through, long? epoch, SimulateConsole console, CancellationToken cancellationToken)
    {
        if (epoch is not null || through is not null)
        {
            return;
        }

        try
        {
            var sequential = await services.GetRequiredService<IQueryHandler<GetSequentialStatusQuery, Result<SequentialStatusView>>>()
                .HandleAsync(new GetSequentialStatusQuery(GetSequentialStatusQuery.SystemScope, null), cancellationToken);
            if (sequential.IsSuccess)
            {
                await console.Out.WriteLineAsync(SimulateOptions.Show(sequential.Value.Line));
            }
            else
            {
                await console.Error.WriteLineAsync("sequential line unavailable");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            _ = failure; // no detail is printed: the line is an extra and the check above stands
            await console.Error.WriteLineAsync("sequential line unavailable");
        }
    }

    internal static bool TryParse(List<string> args, out DateOnly? through, out long? epoch)
    {
        through = null;
        epoch = null;
        for (var i = 0; i < args.Count; i += 2)
        {
            if (i + 1 >= args.Count)
            {
                return false;
            }

            switch (args[i])
            {
                case "--through" when through is null && DateOnly.TryParseExact(args[i + 1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d):
                    through = d;
                    break;
                case "--epoch" when epoch is null && long.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var e):
                    epoch = e;
                    break;
                default:
                    return false;
            }
        }

        return true;
    }
}
