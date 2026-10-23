using System.Globalization;
using XDrishti.Api.Simulate;
using XDrishti.Application.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Api.Cli;

/// <summary>The parsed command line of <c>xd learn policy</c> (spec P6-13). Nothing here reads a file or the database.</summary>
internal sealed record LearnPolicyOptions(IReadOnlyList<CellSource> Sources, MetaPolicyOptions Policy, int HoldOutMonths)
{
    public const string Usage = """
        usage:
          xd learn policy --run RUNKEY:STYLE:EXIT [--run ...] [--split none|style|side|style-side] [--min-kept-share X] [--min-lift-r X] [--min-skill-r X] [--trials N] [--hold-out-months N]
            needs a champion model (xd learn designate) made from the same runs; judges take or skip by the model's probability against taking everything, out of sample
            --trials is a floor on the models, splits and settings looked at, which widen the margins (default 1; the champion's registry count is used if larger); a re-run with other knobs or splits is another trial
            --hold-out-months keeps the last N months out of the search (default 6; 0 keeps none)
            prints the evidence; nothing is stored or applied
        exit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled

        """;

    private static readonly string[] Known = ["--run", "--split", "--min-kept-share", "--min-lift-r", "--min-skill-r", "--trials", "--hold-out-months"];

    /// <summary>Parses the arguments after <c>learn policy</c>; the errors are listed when the options are null.</summary>
    public static (LearnPolicyOptions? Options, IReadOnlyList<string> Errors) Parse(IReadOnlyList<string> args)
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

        var d = new MetaPolicyOptions();
        var split = d.Split;
        if (values.TryGetValue("--split", out var text))
        {
            switch (text)
            {
                case "none":
                    split = MetaSplit.None;
                    break;
                case "style":
                    split = MetaSplit.Style;
                    break;
                case "side":
                    split = MetaSplit.Side;
                    break;
                case "style-side":
                    split = MetaSplit.StyleSide;
                    break;
                default:
                    errors.Add("--split is none, style, side or style-side");
                    break;
            }
        }

        var policy = d with { Split = split, MinKeptShare = Number(values, "--min-kept-share", d.MinKeptShare, errors), MinLiftR = Number(values, "--min-lift-r", d.MinLiftR, errors), MinSkillR = Number(values, "--min-skill-r", d.MinSkillR, errors) };
        var trials = d.Trials;
        if (values.TryGetValue("--trials", out var tr) && !int.TryParse(tr, NumberStyles.Integer, CultureInfo.InvariantCulture, out trials))
        {
            errors.Add("--trials must be a whole number");
        }

        policy = policy with { Trials = trials };
        var hold = 6;
        if (values.TryGetValue("--hold-out-months", out var h) && !int.TryParse(h, NumberStyles.Integer, CultureInfo.InvariantCulture, out hold))
        {
            errors.Add("--hold-out-months must be a whole number");
        }

        return errors.Count > 0 ? (null, errors) : (new LearnPolicyOptions(runs, policy, hold), errors);
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
