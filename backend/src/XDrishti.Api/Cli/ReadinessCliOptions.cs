using System.Globalization;
using XDrishti.Api.Simulate;

namespace XDrishti.Api.Cli;

/// <summary>The parsed command line of <c>xd readiness</c> (spec CF-11, part 2a, ADR 0221). <see cref="Strategy"/> is null for the system card. Nothing here reads a file or the database.</summary>
internal sealed record ReadinessCliOptions(string? Strategy, int? Version)
{
    public const string Usage = """
        usage:
          xd readiness [system]
            the system card (the minimum of the integrity and the weighted mean of the strategy scores, the weakest strategy and sub-score), one line per Lab strategy at its latest version, weakest first, and the enforce setting; the same answer as GET /api/confidence/scorecards
          xd readiness STRATEGY [--version N]
            the scorecard of one strategy version (the latest when no version is given): the score as the minimum of the six sub-scores with each sub-score and what would move it, the label, the hard fails by name, the 15-row edge checklist with each row's input, threshold and producer,
            gates R and S with the rows that stop them, what the card is waiting for, the newest daily snapshot, the settings hash and the enforce setting; the same answer as GET /api/confidence/scorecards/{strategyId}/versions/{version}
            read only: nothing is stored, moved, switched or ordered (turning enforce ON or OFF is the owner's act in the app, with a fresh password); an input that is unavailable prints 'unavailable' with its reason and counts as a fail, never as a pass
        exit codes: 0 done, 1 refused (an unknown strategy or version, a failed read), 2 wrong command line, 3 failure, 130 cancelled

        """;

    /// <summary>Parses the arguments after <c>readiness</c>; the errors are listed when the options are null.</summary>
    public static (ReadinessCliOptions? Options, IReadOnlyList<string> Errors) Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var errors = new List<string>();
        string? strategy = null;
        int? version = null;
        var system = false;
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg == "--version")
            {
                if (i + 1 >= args.Count)
                {
                    errors.Add("'--version' needs a value");
                }
                else if (version is not null)
                {
                    errors.Add("'--version' is given twice");
                    i++;
                }
                else if (int.TryParse(args[++i].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed >= 1)
                {
                    version = parsed;
                }
                else
                {
                    errors.Add($"the version '{SimulateOptions.Show(args[i])}' is not a positive whole number");
                }
            }
            else if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                errors.Add($"unknown argument '{SimulateOptions.Show(arg)}'");
            }
            else if (strategy is not null || system)
            {
                errors.Add($"unexpected argument '{SimulateOptions.Show(arg)}'");
            }
            else if (arg == "system")
            {
                system = true;
            }
            else if (string.IsNullOrWhiteSpace(arg) || arg.Contains('|', StringComparison.Ordinal) || arg.Length > 200 || arg.Any(char.IsControl))
            {
                errors.Add($"the strategy id '{SimulateOptions.Show(arg)}' is empty, has a '|' or a control character, or is over 200 characters");
            }
            else
            {
                strategy = arg.Trim();
            }
        }

        if (system && version is not null)
        {
            errors.Add("'--version' belongs to a strategy, not to the system card");
        }

        if (version is not null && strategy is null && !system)
        {
            errors.Add("'--version' needs a strategy id");
        }

        return errors.Count > 0 ? (null, errors) : (new ReadinessCliOptions(strategy, version), errors);
    }
}
