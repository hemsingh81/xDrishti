using XDrishti.Api.Simulate;

namespace XDrishti.Api.Cli;

/// <summary>The parsed command line of <c>xd reports options</c> (spec P14-16). Nothing here reads a file or the database.</summary>
internal sealed record ReportsOptionsOptions(IReadOnlyList<string> Cut, bool Json)
{
    public const string Usage = """
        usage:
          xd reports options [--cut FAMILY[,FAMILY...]] [--json]
            prints the read-only P14 report: the option archive per underlying (recorded and usable chain sessions, first and last date, gaps by outcome, backfill keys, the spread model state), the newest expression runs
            (paired and unpaired by reason, the model-price, derived-expiry and recent-archive-spread shares, lot feasibility), the pair formations of the last 400 days and the newest verdict of every P14 family, then the limits (always printed)
            a figure with no source is the word unavailable with its reason, never a number; no price, IV or Greek is shown
            --cut lists families as cut (shown as cut and not read; default none); --json prints the same fields as JSON
            nothing is written, no run is started and no setting is changed
        exit codes: 0 done, 1 refused (an unknown family in --cut), 2 wrong command line, 3 failure, 130 cancelled

        """;

    /// <summary>Parses the arguments after <c>reports options</c>; the errors are listed when the options are null.</summary>
    public static (ReportsOptionsOptions? Options, IReadOnlyList<string> Errors) Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var errors = new List<string>();
        var cut = new List<string>();
        var json = false;
        var cutGiven = false;
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg == "--json" && !json)
            {
                json = true;
            }
            else if (arg == "--cut" && !cutGiven)
            {
                cutGiven = true;
                if (i + 1 >= args.Count)
                {
                    errors.Add("'--cut' needs a value");
                    continue;
                }

                foreach (var name in args[++i].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    // The names are checked by the use case, which refuses an unknown family (exit 1) and lists the known ones.
                    if (!cut.Contains(name, StringComparer.Ordinal))
                    {
                        cut.Add(name);
                    }
                }
            }
            else
            {
                errors.Add(arg is "--json" or "--cut" ? $"'{arg}' is given twice" : $"unknown argument '{SimulateOptions.Show(arg)}'");
            }
        }

        return errors.Count > 0 ? (null, errors) : (new ReportsOptionsOptions(cut, json), errors);
    }
}
