using System.Globalization;
using XDrishti.Api.Simulate;

namespace XDrishti.Api.Cli;

/// <summary>The parsed command line of <c>xd exec phase-check</c> (spec P12-12). Nothing here reads a file or the database.</summary>
internal sealed record ExecPhaseCheckOptions(Guid? AccountId, DateOnly? Through)
{
    public const string Usage = """
        usage:
          xd exec phase-check [--account ID] [--through YYYY-MM-DD]
            judges "30 semi-auto sessions with zero reconciliation breaks" from stored records, per Trading account: each session counted, idle, broken or out of mode, the streak, MET / NOT MET / NOT EVALUABLE with its numbers,
            the facts beside the verdict, the boundary fact and the honest limits (always printed); --through defaults to the newest session whose final reconciliation is due
            the verdict is never MET on fewer than 30 counted sessions in a row, a break restarts the count, and an unavailable or missing fact never counts as clean
            nothing is written, no test is run and no setting is changed
        exit codes: 0 done (whatever the verdict says), 1 refused (a bad --through or --account), 2 wrong command line, 3 failure, 130 cancelled

        """;

    /// <summary>Parses the arguments after <c>exec phase-check</c>; the errors are listed when the options are null.</summary>
    public static (ExecPhaseCheckOptions? Options, IReadOnlyList<string> Errors) Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var errors = new List<string>();
        DateOnly? through = null;
        Guid? account = null;
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg is not ("--through" or "--account"))
            {
                errors.Add($"unknown argument '{SimulateOptions.Show(arg)}'");
            }
            else if (i + 1 >= args.Count)
            {
                errors.Add($"'{arg}' needs a value");
            }
            else if ((arg == "--through" && through is not null) || (arg == "--account" && account is not null))
            {
                errors.Add($"'{arg}' is given twice");
                i++;
            }
            else if (arg == "--through")
            {
                if (DateOnly.TryParseExact(args[++i], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                {
                    through = parsed;
                }
                else
                {
                    errors.Add("--through must be YYYY-MM-DD");
                }
            }
            else if (Guid.TryParse(args[++i], out var id))
            {
                account = id;
            }
            else
            {
                errors.Add("--account must be an account id");
            }
        }

        return errors.Count > 0 ? (null, errors) : (new ExecPhaseCheckOptions(account, through), errors);
    }
}
