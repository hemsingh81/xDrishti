using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.OptionExpression;

namespace XDrishti.Api.Cli;

/// <summary>
/// <c>xd options remark --run RUNKEY --profile HASH</c> (spec P14-08): re-marks every trade of a stored cash run as the option version a registered <c>option_buy</c> profile describes, by calling the use case; no decision lives here. Prints the counts by fate, the
/// model-price share and the labels. Exit codes: 0 done (also when nothing new was written), 1 an unknown run or profile or a profile that cannot be re-marked, 2 wrong command line, 3 failure, 130 cancelled.
/// </summary>
internal static class OptionsRemarkCommand
{
    public const string Usage = """
        usage:
          xd options remark --run RUNKEY --profile HASH
            re-marks each trade of the stored cash run (64 hex characters) as a single long option under the registered option_buy profile (64 hex characters); every trade gets one row, paired or unpaired with its reason; the same inputs write nothing twice
            the profile's max_hold_sessions is the planned hold of every buy (the stored run does not say): it must be 1 for an intraday run; rows are labelled planned_hold_sessions
        exit codes: 0 done, 1 unknown run or profile, 2 wrong command line, 3 failure, 130 cancelled

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
            var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<RemarkRunCommand, RemarkRunSummary>>().HandleAsync(command, cancellationToken);
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

    internal static (RemarkRunCommand? Command, List<string> Errors) Parse(IReadOnlyList<string> args)
    {
        var errors = new List<string>();
        string? run = null;
        string? profile = null;
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] == "--run" && i + 1 < args.Count && run is null)
            {
                run = args[++i];
            }
            else if (args[i] == "--profile" && i + 1 < args.Count && profile is null)
            {
                profile = args[++i];
            }
            else
            {
                errors.Add($"unknown, repeated or incomplete argument '{SimulateOptions.Show(args[i])}'");
            }
        }

        if (!IsHash(run))
        {
            errors.Add("--run is the 64-character key of a stored run");
        }

        if (!IsHash(profile))
        {
            errors.Add("--profile is the 64-character hash of a registered expression profile");
        }

        return errors.Count > 0 ? (null, errors) : (new RemarkRunCommand(run!, profile!), errors);
    }

    private static bool IsHash(string? text) => text is { Length: 64 } && text.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
}
