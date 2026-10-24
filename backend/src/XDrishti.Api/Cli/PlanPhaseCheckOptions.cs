using System.Globalization;
using XDrishti.Api.Simulate;

namespace XDrishti.Api.Cli;

/// <summary>The parsed command line of <c>xd plan phase-check</c> (spec P7-18). Nothing here reads a file or the database.</summary>
internal sealed record PlanPhaseCheckOptions(DateOnly? Through)
{
    public const string Usage = """
        usage:
          xd plan phase-check [--through YYYY-MM-DD]
            judges the P7 acceptance criteria from stored records: the last ten trading sessions up to --through (default: the newest planned session whose nightly run is due) with the run, gates, plan and manual-run count of each,
            then each criterion printed MET, NOT MET or NOT EVALUABLE with its numbers, the services and owner switches still off, and the honest limits (always printed)
            the roadmap exit is never MET on fewer than ten consecutive scheduled sessions; a manual run, a failed-closed attempt or a gap in the window breaks the run; a holiday between sessions does not
            nothing is written, no test is run and no setting is changed
        exit codes: 0 done (whatever the criteria say), 1 refused (a bad --through), 2 wrong command line, 3 failure, 130 cancelled

        """;

    /// <summary>Parses the arguments after <c>plan phase-check</c>; the errors are listed when the options are null.</summary>
    public static (PlanPhaseCheckOptions? Options, IReadOnlyList<string> Errors) Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var errors = new List<string>();
        DateOnly? through = null;
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg != "--through")
            {
                errors.Add($"unknown argument '{SimulateOptions.Show(arg)}'");
            }
            else if (i + 1 >= args.Count)
            {
                errors.Add($"'{arg}' needs a value");
            }
            else if (through is not null)
            {
                errors.Add($"'{arg}' is given twice");
                i++;
            }
            else if (DateOnly.TryParseExact(args[++i], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                through = parsed;
            }
            else
            {
                errors.Add("--through must be YYYY-MM-DD");
            }
        }

        return errors.Count > 0 ? (null, errors) : (new PlanPhaseCheckOptions(through), errors);
    }
}
