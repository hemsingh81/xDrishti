using System.Globalization;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.OptionExpression;

namespace XDrishti.Api.Cli;

/// <summary>
/// <c>xd options condors --profile HASH --from DATE --to DATE [--underlying SYMBOL]</c> (spec P14-10): simulates the iron condor of the index range family under a registered <c>iron_condor</c> profile, by calling the use case; no decision lives here. Prints the population key (the <c>--run</c> of
/// the evaluation), the counts by fate and the labels. Exit codes: 0 done (also when nothing new was written), 1 refused (an unknown profile, no stored regime, no measured spread), 2 wrong command line, 3 failure, 130 cancelled.
/// </summary>
internal static class OptionsCondorsCommand
{
    public const string Usage = """
        usage:
          xd options condors --profile HASH --from YYYY-MM-DD --to YYYY-MM-DD [--underlying NSE:NIFTY|NSE:BANKNIFTY]
            simulates the iron condor of the index range family under the registered iron_condor profile (64 hex characters): first session of each week at 09:30 after a range session, shorts k steps out, wings 2 steps beyond, exit at the close before the expiry or at the stop;
            every entry gets one row, paired (four legs) or unpaired with its reason; the same inputs write nothing twice; the population key printed is the --run of 'xd options expression-eval --family expr:iron_condor'
            refused without a stored regime (P6-02) and without a measured half-spread model (an assumption is not enough); research only, no order path exists
        exit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled

        """;

    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(console);
        var (command, errors) = Parse(args);
        if (command is null)
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
            var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<SimulateCondorsCommand, RemarkRunSummary>>().HandleAsync(command, cancellationToken);
            if (result.IsFailure)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(result.Error.Code + ": " + result.Error.Message));
                return XdCommand.Refused;
            }

            foreach (var line in result.Value.Lines)
            {
                await console.Out.WriteLineAsync(SimulateOptions.Show(line));
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

    internal static (SimulateCondorsCommand? Command, List<string> Errors) Parse(IReadOnlyList<string> args)
    {
        var errors = new List<string>();
        string? profile = null, underlying = null;
        DateOnly? from = null, to = null;
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] == "--profile" && i + 1 < args.Count && profile is null)
            {
                profile = args[++i];
            }
            else if (args[i] == "--underlying" && i + 1 < args.Count && underlying is null)
            {
                underlying = args[++i];
            }
            else if (args[i] == "--from" && i + 1 < args.Count && from is null)
            {
                from = Date(args[++i]);
                if (from is null)
                {
                    errors.Add("--from is a date YYYY-MM-DD");
                }
            }
            else if (args[i] == "--to" && i + 1 < args.Count && to is null)
            {
                to = Date(args[++i]);
                if (to is null)
                {
                    errors.Add("--to is a date YYYY-MM-DD");
                }
            }
            else
            {
                errors.Add($"unknown, repeated or incomplete argument '{SimulateOptions.Show(args[i])}'");
            }
        }

        if (profile is not { Length: 64 } || !profile.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f')))
        {
            errors.Add("--profile is the 64-character hash of a registered iron_condor profile");
        }

        if (from is null && !errors.Any(e => e.StartsWith("--from", StringComparison.Ordinal)))
        {
            errors.Add("give --from");
        }

        if (to is null && !errors.Any(e => e.StartsWith("--to", StringComparison.Ordinal)))
        {
            errors.Add("give --to");
        }

        return errors.Count > 0 ? (null, errors) : (new SimulateCondorsCommand(profile!, from!.Value, to!.Value, underlying ?? SimulateCondorsCommand.DefaultUnderlying), errors);
    }

    private static DateOnly? Date(string text) => DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
}
