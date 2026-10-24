using XDrishti.Api.Simulate;
using XDrishti.Domain.Learning;

namespace XDrishti.Api.Cli;

/// <summary>The parsed command line of <c>xd plan digest</c> (spec P7-15). Nothing here reads a file or the database.</summary>
internal sealed record PlanDigestOptions(string? Week, bool Store)
{
    public const string Usage = """
        usage:
          xd plan digest [--week YYYY-Www] [--store]
            builds the weekly learning digest from stored records and PRINTS it; nothing is stored unless --store is given (default week: this week, India time)
            --store keeps it, once per ISO week, only for the current week from its Saturday 10:00 India time or the most recent completed week; an older week can only be printed
            a week that has a digest shows the stored one with --store and writes nothing
            the text says when it was built and, for a finished week, that the state is the build date's
            the digest only lists proposals: nothing is applied, approved or ordered
        exit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled

        """;

    /// <summary>Parses the arguments after <c>plan digest</c>; the errors are listed when the options are null.</summary>
    public static (PlanDigestOptions? Options, IReadOnlyList<string> Errors) Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var errors = new List<string>();
        string? week = null;
        var store = false;
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] == "--store")
            {
                store = true;
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

        return errors.Count > 0 ? (null, errors) : (new PlanDigestOptions(week, store), errors);
    }
}
