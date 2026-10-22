using System.Globalization;
using XDrishti.Api.Simulate;
using XDrishti.Application.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Api.Cli;

/// <summary>The parsed command line of <c>xd learn cells</c> (spec P6-01). Nothing here reads a file or the database.</summary>
internal sealed record LearnCellsOptions(DateOnly AsOf, IReadOnlyList<CellSource> Sources, CellStatsOptions Options)
{
    public const string Usage = """
        usage:
          xd learn cells --as-of YYYY-MM-DD --run RUNKEY:STYLE:EXIT [--run ...] [--half-life-days N] [--prior-strength N] [--min-trades N]
            RUNKEY is the 64-hex key of a stored run, STYLE is intraday or swing, EXIT labels the exit policy the run used (no '|')
        exit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled

        """;

    private static readonly string[] Known = ["--as-of", "--run", "--half-life-days", "--prior-strength", "--min-trades"];

    /// <summary>Parses the arguments after <c>learn cells</c>; the errors are listed when the options are null.</summary>
    public static (LearnCellsOptions? Options, IReadOnlyList<string> Errors) Parse(IReadOnlyList<string> args)
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

        DateOnly asOf = default;
        if (!values.TryGetValue("--as-of", out var text) || !DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out asOf))
        {
            errors.Add("--as-of is required, as YYYY-MM-DD");
        }

        if (runs.Count == 0)
        {
            errors.Add("give at least one --run");
        }

        var defaults = new CellStatsOptions();
        var half = Number(values, "--half-life-days", defaults.RecencyHalfLifeDays, errors);
        var strength = Number(values, "--prior-strength", defaults.PriorStrength, errors);
        var min = (int)Number(values, "--min-trades", defaults.MinTrades, errors);
        return errors.Count > 0 ? (null, errors) : (new LearnCellsOptions(asOf, runs, new CellStatsOptions(half, strength, min)), errors);
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
