using System.Globalization;
using XDrishti.Api.Simulate;

namespace XDrishti.Api.Cli;

/// <summary>The parsed command line of <c>xd plan grade</c> (spec P7-13). Nothing here reads a file or the database.</summary>
internal sealed record PlanGradeOptions(DateOnly? Date)
{
    public const string Usage = """
        usage:
          xd plan grade [--date YYYY-MM-DD]
            grades the tickets (primary, reserve and rejected) of past plans once, by walking each through the stored bars of its session with the simulator's rules
            every grade is labelled 'simulated on plan prices'; a ticket whose bars are not there yet is left for a later run; a grade is never changed
            without --date: every past plan with ungraded tickets (newest 30)
            no order is placed, nothing is approved and no ticket changes state
        exit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled

        """;

    /// <summary>Parses the arguments after <c>plan grade</c>; the errors are listed when the options are null.</summary>
    public static (PlanGradeOptions? Options, IReadOnlyList<string> Errors) Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var errors = new List<string>();
        DateOnly? date = null;
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] != "--date")
            {
                errors.Add($"unknown argument '{SimulateOptions.Show(args[i])}'");
            }
            else if (i + 1 >= args.Count)
            {
                errors.Add("'--date' needs a value");
            }
            else if (date is not null)
            {
                errors.Add("'--date' is given twice");
                i++;
            }
            else if (DateOnly.TryParseExact(args[++i], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                date = parsed;
            }
            else
            {
                errors.Add("--date must be YYYY-MM-DD");
            }
        }

        return errors.Count > 0 ? (null, errors) : (new PlanGradeOptions(date), errors);
    }
}
