using System.Globalization;
using XDrishti.Api.Simulate;
using XDrishti.Application.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Api.Cli;

/// <summary>The parsed command line of <c>xd learn windows</c> (spec P6-14). Nothing here reads a file or the database.</summary>
internal sealed record LearnWindowsOptions(IReadOnlyList<CellSource> Sources, EntryWindowOptions Options, int HoldOutMonths)
{
    public const string Usage = """
        usage:
          xd learn windows --run RUNKEY:STYLE:EXIT [--run ...] [--bucket-minutes N] [--min-trades N] [--min-lift X] [--prior-strength N] [--hold-out-months N]
            RUNKEY is the 64-hex key of a stored run, STYLE is intraday or swing, EXIT labels the exit policy the run used (no '|')
            --hold-out-months keeps the last N months out of the search (default 6; 0 keeps none)
            prints a proposal per cell; nothing is stored or applied
        exit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled

        """;

    private static readonly string[] Known = ["--run", "--bucket-minutes", "--min-trades", "--min-lift", "--prior-strength", "--hold-out-months"];

    /// <summary>Parses the arguments after <c>learn windows</c>; the errors are listed when the options are null.</summary>
    public static (LearnWindowsOptions? Options, IReadOnlyList<string> Errors) Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var errors = new List<string>();
        var runs = new List<CellSource>();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (!Known.Contains(arg, StringComparer.Ordinal))
            {
                errors.Add($"unknown argument '{SimulateOptions.Show(arg)}'");
            }
            else if (i + 1 >= args.Count)
            {
                errors.Add($"'{arg}' needs a value");
            }
            else if (arg == "--run")
            {
                var parts = args[++i].Split(':');
                if (parts.Length == 3)
                {
                    runs.Add(new CellSource(parts[0], parts[1], parts[2]));
                }
                else
                {
                    errors.Add("--run is RUNKEY:STYLE:EXIT");
                }
            }
            else if (!values.TryAdd(arg, args[++i]))
            {
                errors.Add($"'{arg}' is given twice");
            }
        }

        if (runs.Count == 0)
        {
            errors.Add("give at least one --run");
        }

        var d = new EntryWindowOptions();
        var options = new EntryWindowOptions(
            Whole(values, "--bucket-minutes", d.BucketMinutes, errors),
            Whole(values, "--min-trades", d.MinWindowTrades, errors),
            Number(values, "--min-lift", d.MinLift, errors),
            Number(values, "--prior-strength", d.PriorStrength, errors));
        var hold = Whole(values, "--hold-out-months", 6, errors);
        return errors.Count > 0 ? (null, errors) : (new LearnWindowsOptions(runs, options, hold), errors);
    }

    private static int Whole(Dictionary<string, string> values, string key, int fallback, List<string> errors)
    {
        if (!values.TryGetValue(key, out var text))
        {
            return fallback;
        }

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
        {
            return n;
        }

        errors.Add($"{key} must be a whole number");
        return fallback;
    }

    private static double Number(Dictionary<string, string> values, string key, double fallback, List<string> errors)
    {
        if (!values.TryGetValue(key, out var text))
        {
            return fallback;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n))
        {
            return n;
        }

        errors.Add($"{key} must be a number");
        return fallback;
    }
}
