using XDrishti.Domain.Common;

namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>
/// The rules of accepting a run as the baseline (spec P9-09 AC 11, D6): only a gating run with the verdict PASS; a set whose primary pass count is lower than the current baseline's needs a stated reason. The auto option (ADR 0292) accepts only a run no set of which regressed. Pure.
/// </summary>
public static class EvalAcceptance
{
    public const string BelowThreshold = "eval.below_threshold";
    public const string NonGating = "eval.non_gating";
    public const string ReasonRequired = "eval.reason_required";
    public const int ReasonMaxChars = 500;

    /// <summary>The sets (with their counts) that got worse than the baseline's; empty when there is no baseline.</summary>
    public static IReadOnlyList<string> Worse(RunSummary run, RunSummary? baseline)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (baseline is null)
        {
            return [];
        }

        var worse = new List<string>();
        foreach (var set in EvalVerdict.Gating)
        {
            if (run.PrimaryPasses(set) is { } now && baseline.PrimaryPasses(set) is { } then && now < then)
            {
                worse.Add($"{set} {now} < {then}");
            }
        }

        return worse;
    }

    /// <summary>Whether the auto option may accept the run (ADR 0292, ADR 0282): a gating PASS on which no set got worse than the baseline's. Never a run that would need a reason.</summary>
    public static bool AutoEligible(RunSummary run, RunSummary? baseline)
    {
        ArgumentNullException.ThrowIfNull(run);
        return !run.NonGating && run.Verdict == EvalVerdict.Pass && Worse(run, baseline).Count == 0 && !MissesABaselineSet(run, baseline);
    }

    /// <summary>A gating set the baseline has but the run does not: never auto-accepted, since no comparison exists (unavailable never counts as a pass).</summary>
    private static bool MissesABaselineSet(RunSummary run, RunSummary? baseline)
    {
        return baseline is not null && EvalVerdict.Gating.Any(set => baseline.PrimaryPasses(set) is not null && run.PrimaryPasses(set) is null);
    }

    public static Result Check(RunSummary run, RunSummary? baseline, string? reason)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.NonGating)
        {
            return Result.Failure(Error.Conflict(NonGating, "A run with one repeat is for a look only and can never be accepted."));
        }

        if (run.Verdict != EvalVerdict.Pass)
        {
            return Result.Failure(Error.Conflict(BelowThreshold, $"Only a run with the verdict PASS can be accepted; this one is {run.Verdict}."));
        }

        var worse = Worse(run, baseline);
        if (worse.Count > 0 && string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(Error.Validation(ReasonRequired, "A set got worse than the current baseline (" + string.Join("; ", worse) + "): say why in --reason."));
        }

        return reason is { Length: > ReasonMaxChars }
            ? Result.Failure(Error.Validation(ReasonRequired, $"The reason must be at most {ReasonMaxChars} characters."))
            : Result.Success();
    }
}
