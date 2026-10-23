using System.Globalization;
using XDrishti.Api.Simulate;

namespace XDrishti.Api.Cli;

/// <summary>The parsed command line of <c>xd learn phase-check</c> (spec P6-18). Nothing here reads a file or the database.</summary>
internal sealed record LearnPhaseCheckOptions(double? ReplaySeconds)
{
    public const string Usage = """
        usage:
          xd learn phase-check [--replay-seconds X]
            judges the P6 exit criteria from stored records: the model against the baselines, its calibration error against 5 pp, and the time to read and judge the two runs of the newest experiment (measured now)
            each criterion is printed MET, NOT MET or NOT EVALUABLE with its numbers, and the limits (about 2.1 years, few folds, hold-out, modelled fills) are always printed
            --replay-seconds is the time of a full replay that you timed yourself; it is printed as supplied and is not verified. Without it the 10-minute criterion is never MET, because the replay is not measured here
            nothing is written and nothing is trained or tuned
        exit codes: 0 done (whatever the criteria say), 1 refused, 2 wrong command line, 3 failure, 130 cancelled

        """;

    /// <summary>Parses the arguments after <c>learn phase-check</c>; the errors are listed when the options are null.</summary>
    public static (LearnPhaseCheckOptions? Options, IReadOnlyList<string> Errors) Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var errors = new List<string>();
        double? seconds = null;
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (!string.Equals(arg, "--replay-seconds", StringComparison.Ordinal))
            {
                errors.Add($"unknown argument '{SimulateOptions.Show(arg)}'");
            }
            else if (i + 1 >= args.Count)
            {
                errors.Add($"'{arg}' needs a value");
            }
            else if (seconds is not null)
            {
                errors.Add($"'{arg}' is given twice");
                i++;
            }
            else if (double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) && n > 0)
            {
                seconds = n;
            }
            else
            {
                errors.Add("--replay-seconds must be a number above 0");
            }
        }

        return errors.Count > 0 ? (null, errors) : (new LearnPhaseCheckOptions(seconds), errors);
    }
}
