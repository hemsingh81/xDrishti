using System.Globalization;
using XDrishti.Api.Simulate;
using XDrishti.Application.Learning;

namespace XDrishti.Api.Cli;

/// <summary>The parsed command line of <c>xd learn lesson</c> (spec P6-16). Nothing here reads a file or the database.</summary>
internal sealed record LearnLessonOptions(
    string Action,
    IReadOnlyList<CellSource> Sources,
    int MonitorMonths,
    long? ModelId,
    bool DryRun,
    long Id,
    string? FixKind,
    long FixId,
    string Reason)
{
    public const string Usage = """
        usage:
          xd learn lesson scan --run RUNKEY:STYLE:EXIT [--run ...] [--monitor-months N] [--model ID] [--dry-run]
            replays the monitors on stored runs (growing loss tags, drift alarms still on) and opens a lesson for each finding that is not one yet; --dry-run only lists them
          xd learn lesson link --id N (--hypothesis N | --experiment N) --reason TEXT
            links the fix that was tried; nothing is applied
          xd learn lesson close --id N --reason TEXT
            closes a linked lesson with the before and after numbers of the fix's stored experiment (no number is typed); "no measurable change" is a legitimate result
          xd learn lesson dismiss --id N --reason TEXT
          not built: execution_gap, decision_drag (need real fills and decisions), data_quality, drawdown_beyond_band, tail_losses
        exit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled

        """;

    private static readonly string[] Actions = ["scan", "link", "close", "dismiss"];

    private static readonly string[] Known = ["--run", "--monitor-months", "--model", "--id", "--hypothesis", "--experiment", "--reason"];

    /// <summary>Parses the arguments after <c>learn lesson</c>; the errors are listed when the options are null.</summary>
    public static (LearnLessonOptions? Options, IReadOnlyList<string> Errors) Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Count == 0 || !Actions.Contains(args[0], StringComparer.Ordinal))
        {
            return (null, ["give an action: " + string.Join(", ", Actions)]);
        }

        var errors = new List<string>();
        var runs = new List<CellSource>();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var dryRun = false;
        for (var i = 1; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg == "--dry-run")
            {
                dryRun = true;
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

        var action = args[0];
        if (action == "scan")
        {
            if (values.ContainsKey("--id") || values.ContainsKey("--reason") || values.ContainsKey("--hypothesis") || values.ContainsKey("--experiment"))
            {
                errors.Add("--id, --reason, --hypothesis and --experiment do not go with scan");
            }

            if (runs.Count == 0)
            {
                errors.Add("give at least one --run");
            }
        }
        else
        {
            if (dryRun || runs.Count > 0 || values.ContainsKey("--model") || values.ContainsKey("--monitor-months"))
            {
                errors.Add("--run, --model, --monitor-months and --dry-run only go with scan");
            }

            if (!values.TryGetValue("--reason", out var reason) || string.IsNullOrWhiteSpace(reason))
            {
                errors.Add("give --reason");
            }
        }

        var id = action == "scan" ? 0 : Whole(values, "--id", errors, required: true);
        var monthsLong = action == "scan" ? Whole(values, "--monitor-months", errors, required: false, fallback: 6) : 6;
        if (monthsLong is < 1 or > 120)
        {
            errors.Add("--monitor-months is 1 to 120");
        }

        var months = (int)Math.Clamp(monthsLong, 1, 120);
        long? model = action == "scan" && values.ContainsKey("--model") ? Whole(values, "--model", errors, required: false) : null;
        string? kind = null;
        long fixId = 0;
        if (action == "link")
        {
            var hasH = values.ContainsKey("--hypothesis");
            var hasE = values.ContainsKey("--experiment");
            if (hasH == hasE)
            {
                errors.Add("give exactly one of --hypothesis and --experiment");
            }
            else
            {
                kind = hasH ? "hypothesis" : "experiment";
                fixId = Whole(values, hasH ? "--hypothesis" : "--experiment", errors, required: true);
            }
        }
        else if (action != "scan" && (values.ContainsKey("--hypothesis") || values.ContainsKey("--experiment")))
        {
            errors.Add("--hypothesis and --experiment only go with link");
        }

        return errors.Count > 0
            ? (null, errors)
            : (new LearnLessonOptions(action, runs, months, model, dryRun, id, kind, fixId, values.GetValueOrDefault("--reason") ?? string.Empty), errors);
    }

    private static long Whole(Dictionary<string, string> values, string key, List<string> errors, bool required, long fallback = 0)
    {
        if (!values.TryGetValue(key, out var text))
        {
            if (required)
            {
                errors.Add($"give {key}");
            }

            return fallback;
        }

        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
        {
            return n;
        }

        errors.Add($"{key} must be a whole number");
        return fallback;
    }
}
