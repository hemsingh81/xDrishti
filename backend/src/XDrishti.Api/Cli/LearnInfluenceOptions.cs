using System.Globalization;
using XDrishti.Api.Simulate;

namespace XDrishti.Api.Cli;

/// <summary>The parsed command line of <c>xd learn influence</c> (spec CF-10, part 10d-2a). Nothing here reads a file or the database.</summary>
internal sealed record LearnInfluenceOptions(long ModelId)
{
    public const string Usage = """
        usage:
          xd learn influence MODEL-ID
            whether the registered model may influence a decision, term by term (the hold-out, the forward shadow candidates, the calibration detector, the owner's switch), the reasons it may not, the hold-out state and figures, whether it is the champion
            and the three ML switches (ml.influence.enabled, ml.auto_promote, ml.auto_rollback; all OFF by default) with what each means; the same answer as GET /api/learning/models/{id}/influence
          xd learn rollback
            the rollback proposals (model, trigger, numbers, state, actor, when), whether ml.auto_rollback is ON or OFF, the champion now and the proposals that leave (applied) or would leave (open) no qualifying previous champion, as of when they were proposed; the same proposals as GET /api/learning/rollback/proposals
            read only: nothing is evaluated, recorded, promoted, rolled back or switched, and no order exists; applying a rollback stays the owner's act in the app; a figure that is unavailable, refused or not evaluated is never a pass
        exit codes: 0 done, 1 refused (an unknown model, a failed read), 2 wrong command line, 3 failure, 130 cancelled

        """;

    /// <summary>Parses the arguments after <c>learn influence</c>; the errors are listed when the options are null.</summary>
    public static (LearnInfluenceOptions? Options, IReadOnlyList<string> Errors) Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var errors = new List<string>();
        if (args.Count == 0)
        {
            errors.Add("give the MODEL-ID");
        }

        foreach (var extra in args.Skip(1))
        {
            errors.Add($"unknown argument '{SimulateOptions.Show(extra)}'");
        }

        long id = 0;
        if (args.Count > 0 && !(long.TryParse(args[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0))
        {
            errors.Add($"the model id '{SimulateOptions.Show(args[0])}' is not a positive whole number");
        }

        return errors.Count > 0 ? (null, errors) : (new LearnInfluenceOptions(id), errors);
    }
}
