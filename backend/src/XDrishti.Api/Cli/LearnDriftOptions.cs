using System.Globalization;
using XDrishti.Api.Simulate;
using XDrishti.Application.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Api.Cli;

/// <summary>The parsed command line of <c>xd learn drift</c> (spec P6-09). Nothing here reads a file or the database.</summary>
internal sealed record LearnDriftOptions(IReadOnlyList<CellSource> Sources, DriftOptions Options, int MonitorMonths, long? ModelId, IReadOnlyList<string>? Required)
{
    public const string Usage = """
        usage:
          xd learn drift --run RUNKEY:STYLE:EXIT [--run ...] [--monitor-months N] [--model ID [--require NAME,NAME,...]] [--window-trades N] [--below-band-checks N] [--max-calibration-gap-pp X]
            RUNKEY is the 64-hex key of a stored run, STYLE is intraday or swing, EXIT labels the exit policy the run used (no '|')
            replays the last N months (default 6) of the stored runs as the monitored period against the band of the earlier trades: rolling expectancy, CUSUM and, with --model, the calibration of a registered model
            (--model also needs the runs to have features, xd learn features); prints the alarms and the transitions they would suggest; nothing is recorded and no cell changes state
        exit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled

        """;

    private static readonly string[] Known =
    [
        "--run", "--monitor-months", "--model", "--require", "--window-trades", "--below-band-checks", "--max-calibration-gap-pp",
    ];

    /// <summary>Parses the arguments after <c>learn drift</c>; the errors are listed when the options are null.</summary>
    public static (LearnDriftOptions? Options, IReadOnlyList<string> Errors) Parse(IReadOnlyList<string> args)
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

        if (values.ContainsKey("--require") && !values.ContainsKey("--model"))
        {
            errors.Add("--require only goes with --model");
        }

        var d = new DriftOptions();
        var options = d with
        {
            WindowTrades = Whole(values, "--window-trades", d.WindowTrades, errors),
            BelowBandChecks = Whole(values, "--below-band-checks", d.BelowBandChecks, errors),
            MaxCalibrationGapPp = Number(values, "--max-calibration-gap-pp", d.MaxCalibrationGapPp, errors),
        };
        var months = Whole(values, "--monitor-months", 6, errors);
        long? model = null;
        if (values.TryGetValue("--model", out var text))
        {
            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                model = id;
            }
            else
            {
                errors.Add("--model must be a whole number");
            }
        }

        IReadOnlyList<string>? required = values.TryGetValue("--require", out var list) ? [.. list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)] : null;
        return errors.Count > 0 ? (null, errors) : (new LearnDriftOptions(runs, options, months, model, required), errors);
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
