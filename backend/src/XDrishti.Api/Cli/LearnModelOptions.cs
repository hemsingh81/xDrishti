using System.Globalization;
using XDrishti.Api.Simulate;
using XDrishti.Application.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Api.Cli;

/// <summary>The parsed command line of <c>xd learn model</c> (spec P6-07). Nothing here reads a file or the database.</summary>
internal sealed record LearnModelOptions(IReadOnlyList<CellSource> Sources, IReadOnlyList<string>? Required, ValidationOptions Validation, int HoldOutMonths)
{
    public const string Usage = """
        usage:
          xd learn model --run RUNKEY:STYLE:EXIT [--run ...] [--require NAME,NAME,...] [--hold-out-months N] [--trials N] [--min-folds N]
            RUNKEY is the 64-hex key of a stored run with features (xd learn features), STYLE is intraday or swing, EXIT labels the exit policy the run used (no '|')
            builds and keeps the training set, judges the model against the baselines on purged walk-forward folds, trains the final model on the development rows,
            writes its file under data/models and registers it as a challenger whatever the verdict; only an Eligible model can be named champion (xd learn designate)
            --hold-out-months keeps the last N months out of the verdict and the model (default 6; 0 keeps none)
            --trials counts the models and settings looked at, which widens the margins (default 1); --min-folds is the fewest folds for a verdict (default 6)
        exit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled

        """;

    private static readonly string[] Known = ["--run", "--require", "--hold-out-months", "--trials", "--min-folds"];

    /// <summary>Parses the arguments after <c>learn model</c>; the errors are listed when the options are null.</summary>
    public static (LearnModelOptions? Options, IReadOnlyList<string> Errors) Parse(IReadOnlyList<string> args)
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

        var defaults = new ValidationOptions();
        var hold = Whole(values, "--hold-out-months", 6, errors);
        var trials = Whole(values, "--trials", defaults.Trials, errors);
        var minFolds = Whole(values, "--min-folds", defaults.MinFoldsForVerdict, errors);
        IReadOnlyList<string>? required = values.TryGetValue("--require", out var list) ? [.. list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)] : null;
        return errors.Count > 0 ? (null, errors) : (new LearnModelOptions(runs, required, defaults with { Trials = trials, MinFoldsForVerdict = minFolds }, hold), errors);
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
}
