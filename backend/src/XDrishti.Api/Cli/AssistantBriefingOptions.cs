using XDrishti.Api.Simulate;

namespace XDrishti.Api.Cli;

/// <summary>The parsed command line of <c>xd assistant briefing</c> (spec P9-06). Nothing here reads a file or the database.</summary>
internal sealed record AssistantBriefingOptions(string Kind, string? Subject, bool ModelOff, bool Store, bool NewVersion)
{
    public const string Usage = """
        usage:
          xd assistant briefing [--kind daily|weekly] [--subject DATE|YYYY-Www] [--model on|off] [--store] [--new-version]
            PRINTS the fixed-format text of a plan date (default: the newest plan) or an ISO week (default: this week); nothing is stored and the model is not called
            --store keeps the text, once per subject (the first stored wins; a repeat shows the stored one); --store builds it as the services do: the model phrases it only when
              ai.settings.enabled AND Assistant:Briefing:UseModel are both on, a checker rejects any figure not in the stored facts, and the fixed-format text is stored instead
            --model off never calls the model for this build; --model on changes nothing (the two switches decide)
            --new-version (with --store) appends version n+1 although the subject has a text: the owner's act, recorded as cli:xd
            a plan date older than 7 days can only be printed
            the text is only text: nothing is applied, approved or ordered
        exit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled

        """;

    /// <summary>Parses the arguments after <c>assistant briefing</c>; the errors are listed when the options are null.</summary>
    public static (AssistantBriefingOptions? Options, IReadOnlyList<string> Errors) Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var errors = new List<string>();
        var kind = "daily";
        string? subject = null;
        bool modelOff = false, store = false, newVersion = false;
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--store":
                    store = true;
                    break;
                case "--new-version":
                    newVersion = true;
                    break;
                case "--kind" or "--subject" or "--model" when i + 1 >= args.Count:
                    errors.Add($"'{args[i]}' needs a value");
                    break;
                case "--kind":
                    kind = args[++i];
                    if (kind is not ("daily" or "weekly"))
                    {
                        errors.Add("--kind is daily or weekly");
                    }

                    break;
                case "--subject":
                    subject = args[++i];
                    break;
                case "--model":
                    var model = args[++i];
                    if (model is not ("on" or "off"))
                    {
                        errors.Add("--model is on or off");
                    }

                    modelOff = model == "off";
                    break;
                default:
                    errors.Add($"unknown argument '{SimulateOptions.Show(args[i])}'");
                    break;
            }
        }

        if (newVersion && !store)
        {
            errors.Add("--new-version needs --store");
        }

        return errors.Count > 0 ? (null, errors) : (new AssistantBriefingOptions(kind, subject, modelOff, store, newVersion), errors);
    }
}
