namespace XDrishti.Domain.Learning;

/// <summary>The rule for naming a champion (P6-00 D4, ADR 0070): a model may be registered whatever its verdict, but only one that beat both baselines on enough folds, pooled, calibrated and with enough trades (<see cref="ValidationOutcome.Eligible"/>) may be champion.</summary>
public static class ChampionRule
{
    /// <summary>True when a model with this verdict may be named champion.</summary>
    public static bool MayBeChampion(ValidationOutcome verdict) => verdict == ValidationOutcome.Eligible;

    /// <summary>Whether the verdict allows the role; a challenger or a retired model needs no verdict.</summary>
    public static bool Allows(ModelRole role, ValidationOutcome verdict) => role != ModelRole.Champion || MayBeChampion(verdict);
}
