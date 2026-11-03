using System.Globalization;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;
using XDrishti.Application.Pairs;

namespace XDrishti.Api.Cli;

/// <summary>
/// <c>xd pairs run|eval</c> (spec P14-13): parses the line and calls the two use cases; no decision lives here. <c>run</c> prints the ledger count and each quarter's formation and trades; <c>eval</c> prints the verdict of protocol S with the quantities it names as missing. A NOT_EVALUABLE verdict is an answer,
/// not a failure (exit 0). Exit codes: 0 done, 1 refused (an unknown or unreadable input, a spent hold-out, a trial not in the ledger), 2 wrong command line, 3 failure, 130 cancelled.
/// </summary>
internal static class PairsCommand
{
    public const string Usage = """
        usage:
          xd pairs run --from YYYY-MM-DD --to YYYY-MM-DD [--variant ENTRY,LOOKBACK ...]
            for each calendar quarter that touches the dates: 252 daily closes before it, same-sector candidates (correlation 0.8, at most Pairs:MaxCandidates), every candidate appended to the test ledger BEFORE any test, Engle-Granger and Benjamini-Hochberg over all candidates,
            then the selected pairs walked intraday on 5-minute spreads (both legs, the simulator's fills); gross unless Pairs:CostSheetId names a stored sheet; the variants are the six of ENTRY 1.5|2.0|2.5 with LOOKBACK 5|10, all by default
          xd pairs eval --family pairs:intraday-1 [--holdout]
            protocol S over the stored trades of the primary variant (2.0,5): KEEP, DROP, INCONCLUSIVE or NOT_EVALUABLE with each missing quantity named; --holdout reads the last six months once, after a stored development KEEP
        research only: nothing selects, plans or orders a pair
        exit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled

        """;

    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(console);
        var verb = args.Count > 0 ? args[0] : string.Empty;
        var rest = args.Skip(1).ToList();
        var errors = new List<string>();
        RunPairsCommand? run = null;
        EvaluatePairsCommand? eval = null;
        if (verb == "run")
        {
            run = ParseRun(rest, errors);
        }
        else if (verb == "eval")
        {
            eval = ParseEval(rest, errors);
        }
        else
        {
            errors.Add("the verb is 'xd pairs run' or 'xd pairs eval'");
        }

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
            if (run is not null)
            {
                var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<RunPairsCommand, PairsRunSummary>>().HandleAsync(run, cancellationToken);
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

            var evaluated = await scope.ServiceProvider.GetRequiredService<ICommandHandler<EvaluatePairsCommand, ExtensionEvaluationOutcome>>().HandleAsync(eval!, cancellationToken);
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

    /// <summary>Parses the arguments after <c>pairs run</c>; the command is null when there are errors.</summary>
    internal static RunPairsCommand? ParseRun(IReadOnlyList<string> args, List<string> errors)
    {
        DateOnly? from = null, to = null;
        var variants = new List<Domain.PairResearch.PairVariant>();
        for (var i = 0; i < args.Count; i++)
        {
            var name = args[i];
            if (name is not ("--from" or "--to" or "--variant") || i + 1 >= args.Count)
            {
                errors.Add($"unknown or incomplete argument '{SimulateOptions.Show(name)}'");
                continue;
            }

            var value = args[++i];
            if (name == "--variant")
            {
                var variant = Domain.PairResearch.PairVariant.Parse(value);
                if (variant is null)
                {
                    errors.Add($"--variant is ENTRY,LOOKBACK from the grid (1.5|2.0|2.5 and 5|10), not '{SimulateOptions.Show(value)}'");
                }
                else if (variants.Contains(variant))
                {
                    errors.Add($"variant {variant.Label} is given once");
                }
                else
                {
                    variants.Add(variant);
                }

                continue;
            }

            if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                errors.Add($"{name} is a date YYYY-MM-DD, not '{SimulateOptions.Show(value)}'");
            }
            else if ((name == "--from" ? from : to) is not null)
            {
                errors.Add($"'{name}' is given once");
            }
            else if (name == "--from")
            {
                from = date;
            }
            else
            {
                to = date;
            }
        }

        if (from is null || to is null)
        {
            errors.Add("give --from and --to");
        }
        else if (from > to)
        {
            errors.Add("--from is after --to");
        }

        return errors.Count > 0 ? null : new RunPairsCommand(from!.Value, to!.Value, variants.Count == 0 ? null : variants);
    }

    /// <summary>Parses the arguments after <c>pairs eval</c>; the command is null when there are errors.</summary>
    internal static EvaluatePairsCommand? ParseEval(IReadOnlyList<string> args, List<string> errors)
    {
        string? family = null;
        var holdOut = false;
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] == "--holdout")
            {
                holdOut = true;
            }
            else if (args[i] != "--family" || i + 1 >= args.Count)
            {
                errors.Add($"unknown or incomplete argument '{SimulateOptions.Show(args[i])}'");
            }
            else if (family is not null)
            {
                errors.Add("'--family' is given once");
                i++;
            }
            else
            {
                family = args[++i];
                if (family != EvaluatePairsCommand.IntradayFamily)
                {
                    errors.Add($"the family is {EvaluatePairsCommand.IntradayFamily}, not '{SimulateOptions.Show(family)}'");
                }
            }
        }

        if (family is null)
        {
            errors.Add("give --family");
        }

        return errors.Count > 0 || family is null ? null : new EvaluatePairsCommand(family, holdOut);
    }
}
