namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>The verdict of a run (spec P9-09 AC 9): PASS needs data, plans and ideas to pass; a failing set fails the run; a set not run makes it INCOMPLETE; events not evaluable is shown and does not block. Pure.</summary>
public static class EvalVerdict
{
    public const string Pass = "PASS";
    public const string Fail = "FAIL";
    public const string Incomplete = "INCOMPLETE";

    public static readonly string[] Gating = ["data", "plans", "ideas"];

    /// <summary>The verdict of the outcomes of the four sets; a missing set counts as not run.</summary>
    public static string Of(IReadOnlyList<SetOutcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        if (outcomes.Any(o => o.State == SetState.Fail))
        {
            return Fail;
        }

        return Gating.All(set => outcomes.Any(o => o.Set == set && o.State == SetState.Pass)) ? Pass : Incomplete;
    }
}
