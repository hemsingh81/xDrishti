using System.Globalization;
using XDrishti.Api.Simulate;
using XDrishti.Domain.Learning;

namespace XDrishti.Api.Cli;

/// <summary>The parsed command line of <c>xd select cells</c> (spec P7-01). Nothing here reads a file or the database.</summary>
internal sealed record SelectCellsOptions(DateOnly Date, CellState MinState)
{
    public const string Usage = """
        usage:
          xd select cells --date YYYY-MM-DD [--min-state candidate|paper|active]
            runs the stored cells through the eligibility filters and the ranker as of that date: the newest statistics snapshot with an as-of date before it and each cell's state as the changelog stood before that day are used, each filter shows pass, fail or unavailable with its code and numbers
            read-only; a cell is not a ticket, so instrument-level inputs are unavailable and the limits are printed with the list
        exit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled

        """;

    /// <summary>Parses the arguments after <c>select cells</c>; the errors are listed when the options are null.</summary>
    public static (SelectCellsOptions? Options, IReadOnlyList<string> Errors) Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var errors = new List<string>();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg is not ("--date" or "--min-state"))
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

        DateOnly date = default;
        if (!values.TryGetValue("--date", out var text) || !DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            errors.Add("--date YYYY-MM-DD is required");
        }

        var min = CellState.Candidate;
        if (values.TryGetValue("--min-state", out var s))
        {
            if (CellStateExtensions.Parse(s) is { } parsed && parsed is CellState.Candidate or CellState.Paper or CellState.Active)
            {
                min = parsed;
            }
            else
            {
                errors.Add("--min-state is candidate, paper or active");
            }
        }

        return errors.Count > 0 ? (null, errors) : (new SelectCellsOptions(date, min), errors);
    }
}
