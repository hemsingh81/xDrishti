using XDrishti.Api.Simulate;

namespace XDrishti.Api.Cli;

/// <summary>The parsed command line of <c>xd eval</c> (spec P9-09 Contract). Nothing here reads a file or the database (the answers file is read by the command).</summary>
internal sealed record EvalCliOptions(string Verb, IReadOnlyList<string> Sets, int Repeats, string Target, string? AnswersFile, Guid? Run, Guid? Baseline, string? Reason, string Format)
{
    public const string Usage = """
        usage:
          xd eval run [--set data|plans|ideas|events|all]... [--target builtin] [--repeats 3]
            runs the eval ONCE against the fixture database through the eval tool endpoint; the owner's act: nothing starts it by itself
            needs the assistant's master switch ON (it is never set here), a ready model, xd-mcp-eval and the fixture database (scripts/eval-db.sh); a model that is down is reported as model_unavailable, never a pass
            --repeats 1 is for a look only: such a run can never be accepted. Exit code 0 only for a gating run with the verdict PASS
          xd eval grade --answers FILE.jsonl --target hermes
            grades the data answers of another assistant from a file ({set, case_id, repeat, answer, session_id, started_at, finished_at}); no model is called
          xd eval compare RUN [--baseline RUN]    paired comparison with the accepted baseline (or the named run): counts, discordant pairs, the exact sign-test p
          xd eval accept RUN [--reason "TEXT"]    accepts a gating PASS run as the baseline (the owner's act); a set that got worse than the current baseline needs --reason
          xd eval show RUN [--json|--md|--answers]  prints the report of a run (the three files of data/eval/RUN/)
          xd eval seed                              seeds the fixture into the database named by Eval__SeedConnection (a developer step of scripts/eval-db.sh; refuses anything but xdrishti_eval and an empty database)
        exit codes: 0 done (run: PASS), 1 refused, 2 wrong command line, 3 failure, 4 a run that is not PASS, 130 cancelled

        """;

    /// <summary>Parses the arguments after <c>eval</c>; the errors are listed when the options are null.</summary>
    public static (EvalCliOptions? Options, IReadOnlyList<string> Errors) Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var errors = new List<string>();
        if (args.Count == 0 || args[0] is not ("run" or "grade" or "compare" or "accept" or "show" or "seed"))
        {
            return (null, ["name a verb: run, grade, compare, accept, show or seed"]);
        }

        var verb = args[0];
        var sets = new List<string>();
        var repeats = 3;
        var target = verb == "grade" ? "hermes" : "builtin";
        string? answers = null, reason = null;
        Guid? run = null, baseline = null;
        var format = "text";
        for (var i = 1; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--set" when i + 1 < args.Count:
                    sets.Add(args[++i]);
                    break;
                case "--repeats" when i + 1 < args.Count:
                    if (!int.TryParse(args[++i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out repeats))
                    {
                        errors.Add("--repeats is a whole number");
                    }

                    break;
                case "--target" when i + 1 < args.Count:
                    target = args[++i];
                    break;
                case "--answers" when i + 1 < args.Count:
                    answers = args[++i];
                    break;
                case "--baseline" when i + 1 < args.Count:
                    baseline = Guid.TryParse(args[++i], out var b) ? b : null;
                    if (baseline is null)
                    {
                        errors.Add("--baseline is a run id");
                    }

                    break;
                case "--reason" when i + 1 < args.Count:
                    reason = args[++i];
                    break;
                case "--json" or "--md" or "--answers" when verb == "show":
                    format = args[i][2..];
                    break;
                default:
                    if (run is null && Guid.TryParse(args[i], out var id) && verb is "compare" or "accept" or "show")
                    {
                        run = id;
                    }
                    else
                    {
                        errors.Add($"unknown or incomplete argument '{SimulateOptions.Show(args[i])}'");
                    }

                    break;
            }
        }

        if (verb is "compare" or "accept" or "show" && run is null)
        {
            errors.Add("name the run (its id)");
        }

        if (verb == "grade" && answers is null)
        {
            errors.Add("--answers FILE is required");
        }

        if (verb == "run" && sets.Count == 0)
        {
            sets.Add("all");
        }

        return errors.Count > 0 ? (null, errors) : (new EvalCliOptions(verb, sets, repeats, target, answers, run, baseline, reason, format), errors);
    }
}
