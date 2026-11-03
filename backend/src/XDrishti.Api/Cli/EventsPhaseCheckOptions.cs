using XDrishti.Api.Simulate;

namespace XDrishti.Api.Cli;

/// <summary>The parsed command line of <c>xd events phase-check</c> (spec P15-12). Nothing here reads a file or the database.</summary>
internal sealed record EventsPhaseCheckOptions(IReadOnlyList<string> Cut, bool Json)
{
    public const string Usage = """
        usage:
          xd events phase-check [--cut FAMILY[,FAMILY...]] [--json]
            judges the P15 exit "features and filters improve OOS or are dropped" from the stored verdicts of the five families (filter:results, ext:ev-1, ext:fu-1, ext:llm-1, pead), each for the current versions only,
            then prints MET, NOT MET or NOT EVALUABLE with every open family and its missing quantity, the violations (the event filter ON without a current KEEP), the coverage figures and the honest limits (always printed)
            --cut lists families as cut and leaves them out of the overall (default: none cut); --json prints the same fields as JSON
            nothing is written, no evaluation is run, no model is called and no setting is changed
        exit codes: 0 done (whatever the verdict), 1 refused (an unknown family), 2 wrong command line, 3 failure, 130 cancelled

        """;

    /// <summary>Parses the arguments after <c>events phase-check</c>; the errors are listed when the options are null.</summary>
    public static (EventsPhaseCheckOptions? Options, IReadOnlyList<string> Errors) Parse(IReadOnlyList<string> args)
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

        return errors.Count > 0 ? (null, errors) : (new EventsPhaseCheckOptions(cut, json), errors);
    }
}
