using System.Globalization;
using XDrishti.Api.Simulate;
using XDrishti.Application.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Api.Cli;

/// <summary>The parsed command line of <c>xd learn loss-tags</c> (spec P6-15). Nothing here reads a file or the database.</summary>
internal sealed record LearnLossTagsOptions(IReadOnlyList<CellSource> Sources, LossTagOptions Options)
{
    public const string Usage = """
        usage:
          xd learn loss-tags --run RUNKEY:STYLE:EXIT [--run ...] [--gap-min-r X] [--gave-back-mfe-r X] [--high-slippage-r X] [--late-after-minute N] [--wide-stop-fraction X] [--growing-window N] [--show-worst N]
            RUNKEY is the 64-hex key of a stored run, STYLE is intraday or swing, EXIT labels the exit policy the run used (no '|')
            prints the tags of every loss with the numbers behind each; nothing is stored, no lesson is opened
        exit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled

        """;

    private static readonly string[] Known = ["--run", "--gap-min-r", "--gave-back-mfe-r", "--high-slippage-r", "--late-after-minute", "--wide-stop-fraction", "--growing-window", "--show-worst"];

    /// <summary>Parses the arguments after <c>learn loss-tags</c>; the errors are listed when the options are null.</summary>
    public static (LearnLossTagsOptions? Options, IReadOnlyList<string> Errors) Parse(IReadOnlyList<string> args)
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

        var d = new LossTagOptions();
        var options = d with
        {
            GapMinR = Number(values, "--gap-min-r", d.GapMinR, errors),
            GaveBackMfeR = Number(values, "--gave-back-mfe-r", d.GaveBackMfeR, errors),
            HighSlippageR = Number(values, "--high-slippage-r", d.HighSlippageR, errors),
            LateAfterMinute = Whole(values, "--late-after-minute", d.LateAfterMinute, errors),
            WideStopFraction = Number(values, "--wide-stop-fraction", d.WideStopFraction, errors),
            GrowingWindow = Whole(values, "--growing-window", d.GrowingWindow, errors),
            ShowWorst = Whole(values, "--show-worst", d.ShowWorst, errors),
        };
        return errors.Count > 0 ? (null, errors) : (new LearnLossTagsOptions(runs, options), errors);
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
