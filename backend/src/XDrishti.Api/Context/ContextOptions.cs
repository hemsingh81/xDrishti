namespace XDrishti.Api.Context;

/// <summary>Which action of the <c>context</c> verb.</summary>
internal enum ContextAction
{
    /// <summary><c>refresh</c>: write the days that became final.</summary>
    Refresh,

    /// <summary><c>coverage</c>: print what is stored and what is missing.</summary>
    Coverage,
}

/// <summary>The command line of <c>context</c> (P3-01d), parsed strictly and purely: exactly one action and nothing else.</summary>
internal sealed record ContextOptions(ContextAction Action)
{
    public const string Usage =
        "usage:\n"
        + "  context refresh    write the market context days that became final (idempotent; the data job does the same after every update)\n"
        + "  context coverage   print the stored days, what is readable from when, the skipped days and any missing prerequisite (writes nothing)\n";

    /// <summary>Parses the arguments after the verb name; the second element holds the usage errors when the first is null.</summary>
    public static (ContextOptions? Options, IReadOnlyList<string> Errors) Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Count == 0)
        {
            return (null, ["name an action: refresh or coverage"]);
        }

        if (args.Count > 1)
        {
            return (null, ["context takes one action and nothing else"]);
        }

        return args[0] switch
        {
            "refresh" => (new ContextOptions(ContextAction.Refresh), []),
            "coverage" => (new ContextOptions(ContextAction.Coverage), []),
            _ => (null, ["unknown action: use refresh or coverage"]),
        };
    }
}
