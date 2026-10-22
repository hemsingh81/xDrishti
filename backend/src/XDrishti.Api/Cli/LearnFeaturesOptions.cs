using XDrishti.Api.Simulate;

namespace XDrishti.Api.Cli;

/// <summary>The parsed command line of <c>xd learn features</c> (spec P6-04). Nothing here reads a file or the database.</summary>
internal sealed record LearnFeaturesOptions(IReadOnlyList<string> RunKeys)
{
    public const string Usage = """
        usage:
          xd learn features --run RUNKEY [--run ...]
            RUNKEY is the 64-hex key of a stored run; its closed trades get their decision-time features
        exit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled

        """;

    /// <summary>Parses the arguments after <c>learn features</c>; the errors are listed when the options are null.</summary>
    public static (LearnFeaturesOptions? Options, IReadOnlyList<string> Errors) Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var errors = new List<string>();
        var runs = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] != "--run")
            {
                errors.Add($"unknown argument '{SimulateOptions.Show(args[i])}'");
            }
            else if (i + 1 >= args.Count)
            {
                errors.Add("'--run' needs a value");
            }
            else
            {
                runs.Add(args[++i]);
            }
        }

        if (runs.Count == 0)
        {
            errors.Add("give at least one --run");
        }

        return errors.Count > 0 ? (null, errors) : (new LearnFeaturesOptions(runs), errors);
    }
}
