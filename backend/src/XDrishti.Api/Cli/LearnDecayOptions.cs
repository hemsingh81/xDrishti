using XDrishti.Api.Simulate;

namespace XDrishti.Api.Cli;

/// <summary>The parsed command line of <c>xd learn decay</c> (spec CF-12, part 12c-1). Nothing here reads a file or the database.</summary>
internal sealed record LearnDecayOptions(string Strategy)
{
    public const string Usage = """
        usage:
          xd learn decay --strategy ID
            the decay monitor of one strategy's forward series: the reference, the windows against their alert level, twice in a row, the CUSUM, the by-quarter table, the four kill-rule evaluations (fires, holds, held or not_evaluated, with the numbers)
            and the open kill proposals with the automatic setting beside them; the same answer as GET /api/confidence/decay
            read only: nothing is proposed, paused or changed, and no order exists; a figure with no source says unavailable and why, and unavailable or not_evaluated is never a pass
        exit codes: 0 done, 1 refused (no series for the strategy, a bad strategy id), 2 wrong command line, 3 failure, 130 cancelled

        """;

    /// <summary>Parses the arguments after <c>learn decay</c>; the errors are listed when the options are null.</summary>
    public static (LearnDecayOptions? Options, IReadOnlyList<string> Errors) Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var errors = new List<string>();
        string? strategy = null;
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] != "--strategy")
            {
                errors.Add($"unknown argument '{SimulateOptions.Show(args[i])}'");
            }
            else if (i + 1 >= args.Count)
            {
                errors.Add("'--strategy' needs a value");
            }
            else if (strategy is not null)
            {
                errors.Add("'--strategy' is given twice");
                i++;
            }
            else
            {
                strategy = args[++i];
            }
        }

        if (errors.Count == 0 && string.IsNullOrWhiteSpace(strategy))
        {
            errors.Add("give --strategy ID");
        }

        return errors.Count > 0 ? (null, errors) : (new LearnDecayOptions(strategy!.Trim()), errors);
    }
}
