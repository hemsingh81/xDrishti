using XDrishti.Api.Simulate;
using XDrishti.Domain.Learning;

namespace XDrishti.Api.Cli;

/// <summary>The parsed command line of <c>xd plan digest</c> (spec P7-15). Nothing here reads a file or the database.</summary>
internal sealed record PlanDigestOptions(string? Week, bool DryRun)
{
    public const string Usage = """
        usage:
          xd plan digest [--week YYYY-Www] [--dry-run]
            builds the weekly learning digest from stored records and stores it once per ISO week (default: this week, India time)
            a week that has a digest shows the stored one and writes nothing; --dry-run builds and prints, stores nothing
            the digest only lists proposals: nothing is applied, approved or ordered
        exit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled

        """;

    /// <summary>Parses the arguments after <c>plan digest</c>; the errors are listed when the options are null.</summary>
    public static (PlanDigestOptions? Options, IReadOnlyList<string> Errors) Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var errors = new List<string>();
        string? week = null;
        var dryRun = false;
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] == "--dry-run")
            {
                dryRun = true;
            }
            else if (args[i] != "--week")
            {
                errors.Add($"unknown argument '{SimulateOptions.Show(args[i])}'");
            }
            else if (i + 1 >= args.Count)
            {
                errors.Add("'--week' needs a value");
            }
            else if (week is not null)
            {
                errors.Add("'--week' is given twice");
                i++;
            }
            else if (DigestWeek.TryParse(args[++i]) is { } parsed)
            {
                week = parsed.Key;
            }
            else
            {
                errors.Add("--week must be YYYY-Www, for example 2026-W43");
            }
        }

        return errors.Count > 0 ? (null, errors) : (new PlanDigestOptions(week, dryRun), errors);
    }
}
