using System.Globalization;
using XDrishti.Api.Simulate;
using XDrishti.Application.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Api.Cli;

/// <summary>The parsed command line of <c>xd learn counterfactual</c> (spec P6-12). Nothing here reads a file or the database.</summary>
internal sealed record LearnCounterfactualOptions(IReadOnlyList<CellSource> Sources, CounterfactualOptions Counterfactual, int HoldOutMonths, bool Store = false)
{
    public const string Usage = """
        usage:
          xd learn counterfactual --run RUNKEY:STYLE:EXIT [--run ...] [--frozen-months N] [--retrain-months N] [--min-gain X] [--min-judged-windows N] [--min-window-rows N] [--significance-z X] [--trials N] [--hold-out-months N] [--store]
            does continued learning help? a model frozen at a cut date (never refitted) against the same recipe refitted every --retrain-months, scoring the same later trades, out of sample
            --frozen-months is the stopped-learning age asked for, 1 to 12 (default 12; shortened to what the history allows, and printed)
            --min-gain is the log loss gain per trade retraining must show (default 0.005)
            --min-judged-windows (default 3, never below 3), --min-window-rows (default 10, never below 10) and --significance-z (default 1.645, never below 1.645) can only be made stricter
            --trials is a floor on the settings looked at, which widen the margins (default 1). EVERY re-run with a different --retrain-months or --frozen-months is another trial: count it, and raise --trials by one each time, or the verdict is flattered
            --hold-out-months keeps the last N months out (default 6; 0 keeps none)
            --store keeps the result in the insert-only learning reports (once per identical inputs; any changed knob is another row). Without it nothing is stored. Nothing is ever applied
        exit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled

        """;

    private static readonly string[] Known = ["--run", "--frozen-months", "--retrain-months", "--min-gain", "--min-judged-windows", "--min-window-rows", "--significance-z", "--trials", "--hold-out-months"];

    /// <summary>Parses the arguments after <c>learn counterfactual</c>; the errors are listed when the options are null.</summary>
    public static (LearnCounterfactualOptions? Options, IReadOnlyList<string> Errors) Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var errors = new List<string>();
        var runs = new List<CellSource>();
        var store = false;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg == "--store")
            {
                store = true;
            }
            else if (!Known.Contains(arg, StringComparer.Ordinal))
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

        var d = new CounterfactualOptions();
        var options = d with
        {
            FrozenMonths = Whole(values, "--frozen-months", d.FrozenMonths, errors),
            RetrainMonths = Whole(values, "--retrain-months", d.RetrainMonths, errors),
            MinGainLogLoss = Number(values, "--min-gain", d.MinGainLogLoss, errors),
            MinJudgedWindows = Math.Max(d.MinJudgedWindows, Whole(values, "--min-judged-windows", d.MinJudgedWindows, errors)),
            MinWindowRows = Math.Max(d.MinWindowRows, Whole(values, "--min-window-rows", d.MinWindowRows, errors)),
            SignificanceZ = Math.Max(d.SignificanceZ, Number(values, "--significance-z", d.SignificanceZ, errors)),
            Trials = Whole(values, "--trials", d.Trials, errors),
        };
        var hold = Whole(values, "--hold-out-months", 6, errors);
        return errors.Count > 0 ? (null, errors) : (new LearnCounterfactualOptions(runs, options, hold, store), errors);
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
