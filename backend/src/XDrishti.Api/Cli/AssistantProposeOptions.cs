using XDrishti.Api.Simulate;

namespace XDrishti.Api.Cli;

/// <summary>The parsed command line of <c>xd assistant propose</c> (spec P9-08). Nothing here reads a file or the database.</summary>
internal sealed record AssistantProposeOptions(string? Kind, string? Cell, bool DryRun)
{
    public const string Usage = """
        usage:
          xd assistant propose [--kind hypothesis|basket_add|basket_remove] [--cell KEY] [--dry-run]
            builds the pack from the stored statistics and asks the local model for up to three proposals; the engine re-reads the evidence and checks every rule, and only what passes is stored in the suggestions inbox
            --kind and --cell narrow the pack; --dry-run does everything but store: it prints the proposals that would have been stored
            needs Assistant:Proposals:Enabled AND ai.settings.enabled (both off by default) and a ready model; nothing is applied: the owner accepts or rejects on the Suggestions screen
        exit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled

        """;

    /// <summary>Parses the arguments after <c>assistant propose</c>; the errors are listed when the options are null.</summary>
    public static (AssistantProposeOptions? Options, IReadOnlyList<string> Errors) Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var errors = new List<string>();
        string? kind = null;
        string? cell = null;
        var dry = false;
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--dry-run":
                    dry = true;
                    break;
                case "--kind" when i + 1 < args.Count:
                    kind = args[++i];
                    break;
                case "--cell" when i + 1 < args.Count:
                    cell = args[++i];
                    break;
                default:
                    errors.Add($"unknown or incomplete argument '{SimulateOptions.Show(args[i])}'");
                    break;
            }
        }

        return errors.Count > 0 ? (null, errors) : (new AssistantProposeOptions(kind, cell, dry), errors);
    }
}
