using System.Globalization;
using XDrishti.Api.Simulate;

namespace XDrishti.Api.Cli;

/// <summary>The parsed command line of <c>xd plan run</c> (spec P7-08). Nothing here reads a file or the database.</summary>
internal sealed record PlanRunOptions(DateOnly? Date, bool DryRun)
{
    public const string Usage = """
        usage:
          xd plan run [--date YYYY-MM-DD] [--dry-run]
            runs the fail-closed nightly pipeline now for the closed session (default: the last one): the gates are read in order and only when all pass is the plan for the next session published (today an empty skeleton plan)
            a failing or unavailable gate publishes nothing and records one failure row; a session that already has a published plan is left alone
            --dry-run reads every gate and builds, and writes nothing
            a run started here is recorded as manual; no order is placed and nothing is approved
        exit codes: 0 published, already published or a dry run whose gates are green, 1 failed closed (gate and reason printed) or refused, 2 wrong command line, 3 failure, 130 cancelled

        """;

    /// <summary>Parses the arguments after <c>plan run</c>; the errors are listed when the options are null.</summary>
    public static (PlanRunOptions? Options, IReadOnlyList<string> Errors) Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var errors = new List<string>();
        DateOnly? date = null;
        var dry = false;
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg == "--dry-run")
            {
                if (dry)
                {
                    errors.Add("'--dry-run' is given twice");
                }

                dry = true;
            }
            else if (arg != "--date")
            {
                errors.Add($"unknown argument '{SimulateOptions.Show(arg)}'");
            }
            else if (i + 1 >= args.Count)
            {
                errors.Add($"'{arg}' needs a value");
            }
            else if (date is not null)
            {
                errors.Add($"'{arg}' is given twice");
                i++;
            }
            else if (DateOnly.TryParseExact(args[++i], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                date = parsed;
            }
            else
            {
                errors.Add("--date must be YYYY-MM-DD");
            }
        }

        return errors.Count > 0 ? (null, errors) : (new PlanRunOptions(date, dry), errors);
    }
}
