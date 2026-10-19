using XDrishti.Api.Simulate;

namespace XDrishti.Api.CostSheets;

/// <summary>Which of the three actions of the <c>cost-sheet</c> verb.</summary>
internal enum CostSheetAction
{
    /// <summary><c>load &lt;file.json&gt; [--replace]</c></summary>
    Load,

    /// <summary><c>show &lt;id&gt;</c></summary>
    Show,

    /// <summary><c>check &lt;id&gt; &lt;report&gt;...</c></summary>
    Check,
}

/// <summary>The outcome of parsing the command line: the options, or the usage errors (never both, never neither).</summary>
internal sealed record CostSheetParse(CostSheetOptions? Options, IReadOnlyList<string> Errors);

/// <summary>
/// The command line of <c>cost-sheet</c> (spec P3-03f, F4), parsed strictly and purely. The arguments are those after the verb name: an action (<c>load</c>, <c>show</c>, <c>check</c>), its positional arguments, and for <c>load</c> the switch
/// <c>--replace</c>. An unknown option, a missing or surplus argument and a repeated switch are usage errors.
/// </summary>
internal sealed record CostSheetOptions(CostSheetAction Action, string? SheetId, IReadOnlyList<string> Files, bool Replace)
{
    /// <summary>The usage text (stderr on a usage error).</summary>
    public const string Usage =
        "usage:\n"
        + "  cost-sheet load <file.json> [--replace]   store a charge sheet (a new id, or the same rates; --replace overwrites other rates under an id)\n"
        + "  cost-sheet show <id>                      print a stored sheet, its rates hash and its label\n"
        + "  cost-sheet check <id> <report.csv>...     compare the sheet with your original global transaction report files (nothing is written)\n";

    /// <summary>Parses the arguments after the verb name.</summary>
    public static CostSheetParse Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var errors = new List<string>();
        if (args.Count == 0)
        {
            return Fail(errors, "name an action: load, show or check");
        }

        CostSheetAction? action = args[0] switch
        {
            "load" => CostSheetAction.Load,
            "show" => CostSheetAction.Show,
            "check" => CostSheetAction.Check,
            _ => null,
        };
        if (action is null)
        {
            return Fail(errors, "unknown action '" + SimulateOptions.Show(args[0]) + "': use load, show or check");
        }

        var replace = false;
        var positional = new List<string>();
        foreach (var token in args.Skip(1))
        {
            if (token == "--replace")
            {
                if (replace)
                {
                    errors.Add("'--replace' is given more than once");
                }

                replace = true;
            }
            else if (token.StartsWith("--", StringComparison.Ordinal))
            {
                errors.Add("unknown option '" + SimulateOptions.Show(token) + "'");
            }
            else if (string.IsNullOrWhiteSpace(token))
            {
                errors.Add("an argument is empty");
            }
            else
            {
                positional.Add(token);
            }
        }

        if (replace && action != CostSheetAction.Load)
        {
            errors.Add("'--replace' belongs to load only");
        }

        switch (action)
        {
            case CostSheetAction.Load:
                if (positional.Count != 1)
                {
                    errors.Add("load takes one file");
                }

                break;
            case CostSheetAction.Show:
                if (positional.Count != 1)
                {
                    errors.Add("show takes one sheet id");
                }

                break;
            default:
                if (positional.Count < 2)
                {
                    errors.Add("check takes a sheet id and at least one report file");
                }

                break;
        }

        if (errors.Count > 0)
        {
            return new CostSheetParse(null, errors);
        }

        return action == CostSheetAction.Load
            ? new CostSheetParse(new CostSheetOptions(action.Value, null, positional, replace), [])
            : new CostSheetParse(new CostSheetOptions(action.Value, positional[0], [.. positional.Skip(1)], false), []);
    }

    private static CostSheetParse Fail(List<string> errors, string message)
    {
        errors.Add(message);
        return new CostSheetParse(null, errors);
    }
}
