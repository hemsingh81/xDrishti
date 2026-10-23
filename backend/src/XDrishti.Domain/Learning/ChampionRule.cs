namespace XDrishti.Domain.Learning;

/// <summary>
/// The rule for naming a champion (P6-00 D4, ADR 0070): a model may be registered whatever its verdict, but only one that beat both baselines on enough folds, pooled, calibrated and with enough trades (<see cref="ValidationOutcome.Eligible"/>) may be champion.
/// The verdict alone is not trusted, because the command line sets the harness options: the model must also have kept a hold-out, been judged on at least <see cref="ValidationOptions.DefaultMinFolds"/> folds, and been judged with at least as many trials as the
/// registry shows were looked at (the models of its feature version registered up to it).
/// </summary>
public static class ChampionRule
{
    /// <summary>Why a model may not be champion; empty when it may.</summary>
    /// <param name="verdict">The verdict stored at registration.</param>
    /// <param name="holdOutStart">The start of the hold-out that was kept, or null when none was.</param>
    /// <param name="usedFolds">Folds the verdict used.</param>
    /// <param name="trials">The trials the verdict was widened for.</param>
    /// <param name="requiredTrials">The trials the registry shows: the distinct models of the feature version registered up to this one.</param>
    public static IReadOnlyList<string> Reasons(ValidationOutcome verdict, DateOnly? holdOutStart, int usedFolds, int trials, int requiredTrials)
    {
        var reasons = new List<string>();
        if (verdict != ValidationOutcome.Eligible)
        {
            reasons.Add("The verdict is not Eligible.");
        }

        if (holdOutStart is null)
        {
            reasons.Add("No hold-out was kept.");
        }

        if (usedFolds < ValidationOptions.DefaultMinFolds)
        {
            reasons.Add($"The verdict used {usedFolds} folds; at least {ValidationOptions.DefaultMinFolds} are needed.");
        }

        if (trials < requiredTrials)
        {
            reasons.Add($"The verdict allowed for {trials} trials; the registry shows {requiredTrials} models looked at.");
        }

        return reasons;
    }

    /// <summary>True when a model with this record may be named champion.</summary>
    public static bool MayBeChampion(ValidationOutcome verdict, DateOnly? holdOutStart, int usedFolds, int trials, int requiredTrials) =>
        Reasons(verdict, holdOutStart, usedFolds, trials, requiredTrials).Count == 0;

    /// <summary>The reasons the role is not allowed; a challenger or a retired model needs none.</summary>
    public static IReadOnlyList<string> Refusals(ModelRole role, ValidationOutcome verdict, DateOnly? holdOutStart, int usedFolds, int trials, int requiredTrials) =>
        role == ModelRole.Champion ? Reasons(verdict, holdOutStart, usedFolds, trials, requiredTrials) : [];
}
