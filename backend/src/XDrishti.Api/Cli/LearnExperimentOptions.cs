using System.Globalization;
using XDrishti.Api.Simulate;
using XDrishti.Domain.Learning;

namespace XDrishti.Api.Cli;

/// <summary>The parsed command line of <c>xd learn experiment</c> (spec P6-11). Nothing here reads a file or the database.</summary>
internal sealed record LearnExperimentOptions(string BaselineRunKey, string CandidateRunKey, ExperimentKnob Knob, ExperimentOptions Options, int HoldOutMonths)
{
    public const string Usage = """
        usage:
          xd learn experiment --baseline RUNKEY --candidate RUNKEY --knob NAME --kind parameter|exit|slippage --from VALUE --to VALUE [--hold-out-months N] [--min-fold-trades N] [--live-gap-r X] [--trials N]
            the two runs are stored runs made on the same data with the same entries; they differ only in the knob (--from is the baseline value, --to the candidate value; slippage is in basis points)
            a cheaper slippage than the baseline cannot win; fewer than 6 used folds is at best inconclusive; every look is recorded and widens the next one
            the replay that makes the candidate run is a separate step (the Lab or xd simulate) and is not timed here
        exit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled

        """;

    private static readonly string[] Known = ["--baseline", "--candidate", "--knob", "--kind", "--from", "--to", "--hold-out-months", "--min-fold-trades", "--live-gap-r", "--trials"];

    /// <summary>Parses the arguments after <c>learn experiment</c>; the errors are listed when the options are null.</summary>
    public static (LearnExperimentOptions? Options, IReadOnlyList<string> Errors) Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var errors = new List<string>();
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
            else if (!values.TryAdd(arg, args[++i]))
            {
                errors.Add($"'{arg}' is given twice");
            }
        }

        foreach (var required in new[] { "--baseline", "--candidate", "--knob", "--kind", "--from", "--to" })
        {
            if (!values.ContainsKey(required))
            {
                errors.Add($"give {required}");
            }
        }

        var kind = ExperimentKnobKind.Parameter;
        if (values.TryGetValue("--kind", out var kindText) && !Enum.TryParse(kindText, true, out kind))
        {
            errors.Add("--kind is parameter, exit or slippage");
        }

        var d = new ExperimentOptions();
        var options = d with
        {
            MinFoldTrades = Whole(values, "--min-fold-trades", d.MinFoldTrades, errors),
            ExpectedLiveGapR = Number(values, "--live-gap-r", d.ExpectedLiveGapR, errors),
            Trials = Whole(values, "--trials", d.Trials, errors),
        };
        var hold = Whole(values, "--hold-out-months", 6, errors);
        if (errors.Count > 0)
        {
            return (null, errors);
        }

        var knob = new ExperimentKnob(values["--knob"], kind, values["--from"], values["--to"]);
        return (new LearnExperimentOptions(values["--baseline"], values["--candidate"], knob, options, hold), errors);
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
