using XDrishti.Api.Simulate;

namespace XDrishti.Api.Cli;

/// <summary>The parsed command line of <c>xd assistant draft</c> (spec P9-07b). Nothing here reads a file or the database.</summary>
internal sealed record AssistantDraftOptions(string Idea, bool Show)
{
    public const string Usage = """
        usage:
          xd assistant draft "<idea>" [--show]
            asks the model for a strategy DRAFT of the idea (10 to 2,000 characters), waits for the job and prints its id and status; --show also prints what the model understood, the values it chose, the checks' findings and the text
            needs Assistant:Drafts:Enabled AND ai.settings.enabled (both off by default); the draft is only text: nothing is stored as a strategy and no test is started
            there is no confirm verb: confirming is the owner's act on the Assistant screen (the switch and a fresh password); a draft made here is never an auto candidate
        exit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled

        """;

    /// <summary>Parses the arguments after <c>assistant draft</c>; the errors are listed when the options are null.</summary>
    public static (AssistantDraftOptions? Options, IReadOnlyList<string> Errors) Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var errors = new List<string>();
        string? idea = null;
        var show = false;
        foreach (var arg in args)
        {
            if (arg == "--show")
            {
                show = true;
            }
            else if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                errors.Add($"unknown argument '{SimulateOptions.Show(arg)}'");
            }
            else if (idea is null)
            {
                idea = arg;
            }
            else
            {
                errors.Add("give the idea as one quoted argument");
            }
        }

        if (idea is null)
        {
            errors.Add("the idea is missing");
        }

        return errors.Count > 0 ? (null, errors) : (new AssistantDraftOptions(idea!, show), errors);
    }
}
