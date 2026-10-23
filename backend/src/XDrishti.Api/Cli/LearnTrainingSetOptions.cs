using System.Globalization;
using XDrishti.Api.Simulate;
using XDrishti.Application.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Api.Cli;

/// <summary>The parsed command line of <c>xd learn training-set</c> (spec P6-04). Nothing here reads a file or the database.</summary>
internal sealed record LearnTrainingSetOptions(IReadOnlyList<CellSource> Sources, CellStatsOptions Options, IReadOnlyList<string>? Required)
{
    public const string Usage = """
        usage:
          xd learn training-set --run RUNKEY:STYLE:EXIT [--run ...] [--require NAME,NAME,...] [--half-life-days N] [--prior-strength N] [--min-trades N]
            RUNKEY is the 64-hex key of a stored run with features (xd learn features), STYLE is intraday or swing, EXIT labels the exit policy the run used (no '|')
            --require names the features a row must have known (default: the catalog's default set)
        exit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled

        """;

    private static readonly string[] Known = ["--run", "--require", "--half-life-days", "--prior-strength", "--min-trades"];

    /// <summary>Parses the arguments after <c>learn training-set</c>; the errors are listed when the options are null.</summary>
    public static (LearnTrainingSetOptions? Options, IReadOnlyList<string> Errors) Parse(IReadOnlyList<string> args)
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

        var defaults = new CellStatsOptions();
        var half = Number(values, "--half-life-days", defaults.RecencyHalfLifeDays, errors);
        var strength = Number(values, "--prior-strength", defaults.PriorStrength, errors);
        var min = defaults.MinTrades;
        if (values.TryGetValue("--min-trades", out var minText) && !int.TryParse(minText, NumberStyles.Integer, CultureInfo.InvariantCulture, out min))
        {
            errors.Add("--min-trades must be a whole number");
        }

        IReadOnlyList<string>? required = values.TryGetValue("--require", out var list) ? [.. list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)] : null;
        return errors.Count > 0 ? (null, errors) : (new LearnTrainingSetOptions(runs, new CellStatsOptions(half, strength, min), required), errors);
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
