namespace XDrishti.Api.Context;

/// <summary>Which action of the <c>context</c> verb.</summary>
internal enum ContextAction
{
    /// <summary><c>refresh</c>: write the days that became final.</summary>
    Refresh,

    /// <summary><c>coverage</c>: print what is stored and what is missing.</summary>
    Coverage,

    /// <summary><c>rebuild-breadth --from --to [--write]</c>: recompute the breadth of stored days (dry run unless <c>--write</c>).</summary>
    RebuildBreadth,
}

/// <summary>The command line of <c>context</c> (P3-01d), parsed strictly and purely: exactly one action and nothing else.</summary>
internal sealed record ContextOptions(ContextAction Action, DateOnly? From = null, DateOnly? To = null, bool Write = false)
{
    public const string Usage =
        "usage:\n"
        + "  context refresh    write the market context days that became final (idempotent; the data job does the same after every update)\n"
        + "  context coverage   print the stored days, what is readable from when, the skipped days and any missing prerequisite (writes nothing)\n"
        + "  context rebuild-breadth --from <yyyy-mm-dd> --to <yyyy-mm-dd> [--write]\n"
        + "                     recompute the breadth of the owner's tracked shares for stored days (at most 400 sessions); a dry run that prints the old and new figure of each changed day unless --write\n";

    /// <summary>Parses the arguments after the verb name; the second element holds the usage errors when the first is null.</summary>
    public static (ContextOptions? Options, IReadOnlyList<string> Errors) Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Count == 0)
        {
            return (null, ["name an action: refresh, coverage or rebuild-breadth"]);
        }

        if (args[0] == "rebuild-breadth")
        {
            return ParseRebuild(args);
        }

        if (args.Count > 1)
        {
            return (null, ["context takes one action and nothing else"]);
        }

        return args[0] switch
        {
            "refresh" => (new ContextOptions(ContextAction.Refresh), []),
            "coverage" => (new ContextOptions(ContextAction.Coverage), []),
            _ => (null, ["unknown action: use refresh, coverage or rebuild-breadth"]),
        };
    }

    private static (ContextOptions? Options, IReadOnlyList<string> Errors) ParseRebuild(IReadOnlyList<string> args)
    {
        DateOnly? from = null;
        DateOnly? to = null;
        var write = false;
        var errors = new List<string>();
        for (var i = 1; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--write":
                    write = true;
                    break;
                case "--from" or "--to":
                    var name = args[i];
                    if (i + 1 >= args.Count || !DateOnly.TryParseExact(args[i + 1], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date))
                    {
                        errors.Add(name + " needs a date as yyyy-mm-dd");
                        i++;
                        break;
                    }

                    if (name == "--from")
                    {
                        from = date;
                    }
                    else
                    {
                        to = date;
                    }

                    i++;
                    break;
                default:
                    errors.Add("unknown option for rebuild-breadth: " + args[i]);
                    break;
            }
        }

        if (from is null)
        {
            errors.Add("--from is required");
        }

        if (to is null)
        {
            errors.Add("--to is required");
        }

        return errors.Count > 0 ? (null, errors) : (new ContextOptions(ContextAction.RebuildBreadth, from, to, write), []);
    }
}
