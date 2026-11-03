using XDrishti.Api.Simulate;

namespace XDrishti.Api.Cli;

/// <summary>The parsed command line of <c>xd learn extension-eval</c> (spec P15-10). Nothing here reads a file or the database.</summary>
internal sealed record LearnExtensionEvalOptions(string Family, bool HoldOut)
{
    /// <summary>The families of the evaluation; a test pins this list to the domain's (the five of P15 and, from P14-11, <c>ext:iv-1</c>, which the P15 phase check never iterates).</summary>
    public static readonly IReadOnlyList<string> Families = ["filter:results", "ext:ev-1", "ext:fu-1", "ext:llm-1", "pead", "ext:iv-1"];

    public const string Usage = """
        usage:
          xd learn extension-eval --family filter:results|ext:ev-1|ext:fu-1|ext:llm-1|pead|ext:iv-1 [--holdout]
            evaluates one family by its pre-registered protocol (F, X, P or L; ext:iv-1 is protocol X on the options preset: quarterly folds, day blocks), counts each trial in the test ledger before any number is computed and stores
            the verdict (KEEP, DROP, INCONCLUSIVE or NOT_EVALUABLE, with the missing quantity named) as an insert-only record; a second run stores nothing new
            --holdout reads the reserved last four seasons, once per family and version, only after a stored development KEEP; a second --holdout is refused (extensions.holdout_used)
            a KEEP is a record: it switches nothing on; only the owner's auto option of the event-risk filter reads a filter verdict
        exit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled

        """;

    /// <summary>Parses the arguments after <c>learn extension-eval</c>; the errors are listed when the options are null.</summary>
    public static (LearnExtensionEvalOptions? Options, IReadOnlyList<string> Errors) Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var errors = new List<string>();
        string? family = null;
        var holdOut = false;
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] == "--holdout")
            {
                holdOut = true;
            }
            else if (args[i] != "--family")
            {
                errors.Add($"unknown argument '{SimulateOptions.Show(args[i])}'");
            }
            else if (i + 1 >= args.Count)
            {
                errors.Add("'--family' needs a value");
            }
            else if (family is not null)
            {
                errors.Add("'--family' is given once");
                i++;
            }
            else
            {
                family = args[++i];
                if (!Families.Contains(family, StringComparer.Ordinal))
                {
                    errors.Add($"the family is one of {string.Join(", ", Families)}, not '{SimulateOptions.Show(family)}'");
                }
            }
        }

        if (family is null)
        {
            errors.Add("give --family");
        }

        return errors.Count > 0 || family is null ? (null, errors) : (new LearnExtensionEvalOptions(family, holdOut), errors);
    }
}
