namespace XDrishti.Domain.LiveTrading;

/// <summary>
/// The scale of a live account's risk (spec P11-04, ADR 0148), pure. With no step there is no scale and so no live allocation. A hold blocks raising only: it does not change the scale. The weekly halve and the halt (scale 0) belong to the evaluator of P11-04b.
/// </summary>
public static class RampPolicy
{
    /// <summary>The factors when nothing is configured: the ceilings.</summary>
    public static IReadOnlyList<double> DefaultFactors => RampSteps.Ceilings;

    /// <summary>Three factors, each above 0, finite, not above the ceiling of its step and strictly rising.</summary>
    public static bool FactorsAreValid(IReadOnlyList<double>? factors)
    {
        if (factors is null || factors.Count != RampSteps.Ceilings.Count)
        {
            return false;
        }

        for (var i = 0; i < factors.Count; i++)
        {
            if (!double.IsFinite(factors[i]) || factors[i] <= 0 || factors[i] > RampSteps.Ceilings[i] || (i > 0 && factors[i] <= factors[i - 1]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The scale in force for the state, or null when the account has no step (or a factor list that is not valid).</summary>
    public static double? Scale(RampState? state, IReadOnlyList<double> factors)
    {
        ArgumentNullException.ThrowIfNull(factors);
        return state is { } s && RampSteps.IsStep(s.Step) && FactorsAreValid(factors) ? factors[s.Step - 1] : null;
    }
}
