using System.Globalization;
using XDrishti.Api.Simulate;
using XDrishti.Application.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Api.Cli;

/// <summary>The parsed command line of <c>xd learn hypothesis</c> (spec P6-10). Nothing here reads a file or the database.</summary>
internal sealed record LearnHypothesisOptions(
    string Action,
    HypothesisSource Source,
    string? Baseline,
    string? Candidate,
    string? Parameter,
    string? Cell,
    ExperimentKnob? Knob,
    string Reason,
    long Id,
    HypothesisGrid Grid,
    ExperimentOptions Experiment,
    int HoldOutMonths,
    IReadOnlyList<CellSource> Sources,
    DateOnly Since,
    int MinNewTrades,
    int MonitorMonths)
{
    public const string Usage = """
        usage:
          xd learn hypothesis grid --baseline RUNKEY --parameter NAME [--grid-step-pct X] [--grid-steps N]
            lists the grid values around a parameter of a stored run (deterministic, bounded); nothing is written
          xd learn hypothesis propose --baseline RUNKEY --knob NAME --kind parameter|exit|slippage --from V --to V --reason TEXT [--source owner|grid] [--cell KEY]
            writes one hypothesis down (one knob; at most 3 open); a parameter knob must be a parameter of the baseline run at its stored value; a grid source must use a grid value
          xd learn hypothesis approve|reject|expire|accept --id N --reason TEXT
            moves it by the state table (the owner approves; accepting needs a Better experiment and only records the verdict, it applies nothing)
          xd learn hypothesis start --id N --candidate RUNKEY
            attaches the candidate run (made separately with the one knob changed); refused unless it differs from the baseline in that knob only
          xd learn hypothesis judge --id N [--hold-out-months N] [--min-fold-trades N] [--live-gap-r X] [--trials N]
            judges it with the experiment of xd learn experiment (recorded, and a trial)
          xd learn hypothesis triggers --run RUNKEY:STYLE:EXIT [--run ...] --since YYYY-MM-DD [--min-new-trades N] [--monitor-months N]
            lists cells with enough new trades or a drift alarm in stored runs; proposals only, nothing is written
        exit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled

        """;

    private static readonly string[] Actions = ["grid", "propose", "approve", "reject", "expire", "accept", "start", "judge", "triggers"];

    private static readonly string[] Known =
    [
        "--baseline", "--candidate", "--parameter", "--cell", "--knob", "--kind", "--from", "--to", "--reason", "--source", "--id", "--grid-step-pct", "--grid-steps", "--hold-out-months", "--min-fold-trades", "--live-gap-r", "--trials",
        "--run", "--since", "--min-new-trades", "--monitor-months",
    ];

    /// <summary>Parses the arguments after <c>learn hypothesis</c>; the errors are listed when the options are null.</summary>
    public static (LearnHypothesisOptions? Options, IReadOnlyList<string> Errors) Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var errors = new List<string>();
        if (args.Count == 0 || !Actions.Contains(args[0], StringComparer.Ordinal))
        {
            return (null, ["give an action: " + string.Join(", ", Actions)]);
        }

        var action = args[0];
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var sources = new List<CellSource>();
        for (var i = 1; i < args.Count; i++)
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
                    sources.Add(new CellSource(parts[0], parts[1], parts[2]));
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

        foreach (var required in Required(action))
        {
            if (!values.ContainsKey(required))
            {
                errors.Add($"give {required}");
            }
        }

        if (action == "triggers" && sources.Count == 0)
        {
            errors.Add("give at least one --run");
        }

        var source = HypothesisSource.Owner;
        if (values.TryGetValue("--source", out var sourceText) && !Enum.TryParse(sourceText, true, out source))
        {
            errors.Add("--source is owner or grid");
        }

        var kind = ExperimentKnobKind.Parameter;
        if (values.TryGetValue("--kind", out var kindText) && !Enum.TryParse(kindText, true, out kind))
        {
            errors.Add("--kind is parameter, exit or slippage");
        }

        var since = default(DateOnly);
        if (values.TryGetValue("--since", out var sinceText) && !DateOnly.TryParseExact(sinceText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out since))
        {
            errors.Add("--since is a date, yyyy-MM-dd");
        }

        var dg = new HypothesisGrid();
        var grid = dg with { StepPct = Number(values, "--grid-step-pct", dg.StepPct, errors), Steps = (int)Whole(values, "--grid-steps", dg.Steps, errors) };
        var d = new ExperimentOptions();
        var experiment = d with
        {
            MinFoldTrades = (int)Whole(values, "--min-fold-trades", d.MinFoldTrades, errors),
            ExpectedLiveGapR = Number(values, "--live-gap-r", d.ExpectedLiveGapR, errors),
            Trials = (int)Whole(values, "--trials", d.Trials, errors),
        };
        var id = Whole(values, "--id", 0, errors);
        var hold = (int)Whole(values, "--hold-out-months", 6, errors);
        var minNew = (int)Whole(values, "--min-new-trades", HypothesisTriggers.DefaultMinNewTrades, errors);
        var monitor = (int)Whole(values, "--monitor-months", 6, errors);
        if (errors.Count > 0)
        {
            return (null, errors);
        }

        var knob = action == "propose" ? new ExperimentKnob(values["--knob"], kind, values["--from"], values["--to"]) : null;
        string? Get(string key) => values.GetValueOrDefault(key);
        return (new LearnHypothesisOptions(action, source, Get("--baseline"), Get("--candidate"), Get("--parameter"), Get("--cell"), knob, Get("--reason") ?? string.Empty, id, grid, experiment, hold, sources, since, minNew, monitor), errors);
    }

    private static string[] Required(string action) => action switch
    {
        "grid" => ["--baseline", "--parameter"],
        "propose" => ["--baseline", "--knob", "--kind", "--from", "--to", "--reason"],
        "approve" or "reject" or "expire" or "accept" => ["--id", "--reason"],
        "start" => ["--id", "--candidate"],
        "judge" => ["--id"],
        _ => ["--since"],
    };

    private static long Whole(Dictionary<string, string> values, string key, long fallback, List<string> errors)
    {
        if (!values.TryGetValue(key, out var text))
        {
            return fallback;
        }

        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n is >= 0 and <= int.MaxValue)
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
