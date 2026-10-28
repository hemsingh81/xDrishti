using System.Globalization;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.LiveTrading;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Cli;

/// <summary>
/// <c>xd live phase-check</c> (spec P11-06): prints the small-live phase check of the live account, recomputed from the stored records. Read only: it writes nothing, runs no test, moves no ramp step and reads nothing from Dhan.
/// The exit code is 0 whatever the verdicts say; 1 refused, 2 wrong command line, 3 failure, 130 cancelled.
/// </summary>
internal static class LivePhaseCheckCommand
{
    public const string Usage = """
        usage:
          xd live phase-check [--through YYYY-MM-DD] [--account GUID]
            prints MET, NOT MET or NOT EVALUABLE for criteria C1 to C8 and the integrity line, the week lines, what is printed and not judged, the blockers and the limits
            reads stored records only; the exit (exit_confirmed) is your act in the Ramp tab
        exit codes: 0 done (whatever the verdicts), 1 refused, 2 wrong command line, 3 failure, 130 cancelled

        """;

    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        var (through, account, errors) = Parse(args);
        if (errors.Count > 0)
        {
            foreach (var error in errors)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(error));
            }

            await console.Error.WriteAsync(Usage);
            return XdCommand.UsageError;
        }

        try
        {
            await using var scope = services.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<IQueryHandler<LivePhaseCheckQuery, Result<LivePhaseCheckView>>>();
            var result = await handler.HandleAsync(new LivePhaseCheckQuery(account, through), cancellationToken);
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

    internal static (DateOnly? Through, Guid? Account, List<string> Errors) Parse(IReadOnlyList<string> args)
    {
        var errors = new List<string>();
        DateOnly? through = null;
        Guid? account = null;
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--through" when i + 1 < args.Count:
                    if (DateOnly.TryParseExact(args[++i], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                    {
                        through = d;
                    }
                    else
                    {
                        errors.Add("--through is YYYY-MM-DD");
                    }

                    break;
                case "--account" when i + 1 < args.Count:
                    if (Guid.TryParse(args[++i], out var g))
                    {
                        account = g;
                    }
                    else
                    {
                        errors.Add("--account is a GUID");
                    }

                    break;
                default:
                    errors.Add($"unknown or incomplete option '{SimulateOptions.Show(args[i])}'");
                    break;
            }
        }

        return (through, account, errors);
    }
}
