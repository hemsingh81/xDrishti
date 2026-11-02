using XDrishti.Api.Simulate;

namespace XDrishti.Api.Cli;

/// <summary>The parsed command line of <c>xd learn features</c> (spec P6-04). Nothing here reads a file or the database.</summary>
internal sealed record LearnFeaturesOptions(IReadOnlyList<string> RunKeys, string? Extension = null)
{
    public const string Usage = """
        usage:
          xd learn features --run RUNKEY [--run ...] [--extension ev-1|fu-1]
            RUNKEY is the 64-hex key of a stored run; its closed trades get their decision-time features (tf-1)
            --extension ev-1|fu-1 extracts the event or the fundamental features of the trades instead (P15-05); a second call stores nothing
        exit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled

        """;

    /// <summary>Parses the arguments after <c>learn features</c>; the errors are listed when the options are null.</summary>
    public static (LearnFeaturesOptions? Options, IReadOnlyList<string> Errors) Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var errors = new List<string>();
        var runs = new List<string>();
        string? extension = null;
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] is not ("--run" or "--extension"))
            {
                errors.Add($"unknown argument '{SimulateOptions.Show(args[i])}'");
            }
            else if (i + 1 >= args.Count)
            {
                errors.Add($"'{args[i]}' needs a value");
            }
            else if (args[i] == "--run")
            {
                runs.Add(args[++i]);
            }
            else if (extension is not null)
            {
                errors.Add("'--extension' is given once");
                i++;
            }
            else
            {
                extension = args[++i];
                if (extension is not ("ev-1" or "fu-1"))
                {
                    errors.Add($"the extension is ev-1 or fu-1, not '{SimulateOptions.Show(extension)}'");
                }
            }
        }

        if (runs.Count == 0)
        {
            errors.Add("give at least one --run");
        }

        return errors.Count > 0 ? (null, errors) : (new LearnFeaturesOptions(runs, extension), errors);
    }
}
