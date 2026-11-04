using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;
using XDrishti.Application.OptionExpression;

namespace XDrishti.Api.Cli;

/// <summary>
/// <c>xd options expression-eval --family expr:option_buy --run RUNKEY [--profile HASH] [--holdout]</c> (spec P14-09): parses the line and calls the use case; no decision lives here. Prints the verdict of protocol O with the quantities it names as missing, then the summary lines.
/// A NOT_EVALUABLE verdict is an answer, not a failure (exit 0). Exit codes: 0 done, 1 refused (an unknown run, profile or family, a run that was not re-marked, a spent hold-out, a trial not in the ledger), 2 wrong command line, 3 failure, 130 cancelled.
/// </summary>
internal static class OptionsExpressionEvalCommand
{
    public const string Usage = """
        usage:
          xd options expression-eval --family expr:option_buy|expr:debit_spread|expr:credit_spread|expr:iron_condor --run RUNKEY [--profile HASH] [--holdout]
            protocol R for expr:iron_condor (--run is the population key 'xd options condors' printed; the condor has no cash twin) and protocol O for the others; credit_spread and iron_condor are NOT_EVALUABLE regime_unavailable without a stored regime and spread_not_measured without a measured spread model, counting no trial; protocol O over the out-of-sample paired trades of a full-test cash run (64 hex characters) re-marked under the profile (64 hex characters; the pre-registered primary profile when omitted): the trial is appended to the test ledger BEFORE any figure is read;
            KEEP, DROP, INCONCLUSIVE or NOT_EVALUABLE with each missing quantity named; a gross result is INCONCLUSIVE whichever way it points; --holdout (primary profile only: the family has one hold-out reading, consumed by whichever profile uses it) reads the last six months once, after a stored development KEEP
        research only: the expression stays cash; nothing selects, plans or orders
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
            var evaluated = await scope.ServiceProvider.GetRequiredService<ICommandHandler<EvaluateExpressionCommand, ExtensionEvaluationOutcome>>().HandleAsync(command, cancellationToken);
            if (evaluated.IsFailure)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(evaluated.Error.Code + ": " + evaluated.Error.Message));
                return XdCommand.Refused;
            }

            foreach (var record in evaluated.Value.Records)
            {
                var missing = record.Missing.Count == 0 ? string.Empty : " (" + string.Join("; ", record.Missing.Select(SimulateOptions.Show)) + ")";
                await console.Out.WriteLineAsync($"{evaluated.Value.Family} {record.Protocol} {record.VersionKey}: {record.Verdict}{missing}{(record.HoldOutRead ? " [hold-out read]" : string.Empty)} {record.EvaluationId[..12]}{(record.Stored ? string.Empty : " (already stored; nothing new)")}");
            }

            foreach (var line in (evaluated.Value.Note ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries))
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

    /// <summary>Parses the arguments after <c>options expression-eval</c>; the command is null when there are errors.</summary>
    internal static (EvaluateExpressionCommand? Command, List<string> Errors) Parse(IReadOnlyList<string> args)
    {
        var errors = new List<string>();
        string? family = null, run = null, profile = null;
        var holdOut = false;
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] == "--holdout" && !holdOut)
            {
                holdOut = true;
            }
            else if (args[i] == "--family" && i + 1 < args.Count && family is null)
            {
                family = args[++i];
            }
            else if (args[i] == "--run" && i + 1 < args.Count && run is null)
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

        if (family is null)
        {
            errors.Add("give --family");
        }

        if (!IsHash(run))
        {
            errors.Add("--run is the 64-character key of a stored full-test run");
        }

        if (profile is not null && !IsHash(profile))
        {
            errors.Add("--profile is the 64-character hash of a registered expression profile");
        }

        return errors.Count > 0 ? (null, errors) : (new EvaluateExpressionCommand(family!, run!, profile, holdOut), errors);
    }

    private static bool IsHash(string? text) => text is { Length: 64 } && text.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
}
