using XDrishti.Indicators;

namespace XDrishti.Strategies.Compilation;

/// <summary>Several conditions that run over one feature frame (spec P3-02, <i>Contract</i>).</summary>
public static class ConditionSet
{
    /// <summary>
    /// Merges the plans of several conditions into one: the union of their features without duplicates (equal canonical specs are one feature, so the frame computes it once), the largest history depth, and the needs
    /// of the frame. The conditions may have been compiled for different evaluation timeframes and parameters; each keeps its own delegate and is bound separately (<see cref="CompiledPlan.BindAll"/>). An empty list gives
    /// a plan without features and a depth of 1. The inputs are not changed and the plan keeps no reference to the list.
    /// </summary>
    /// <exception cref="ArgumentNullException">The list or one of its conditions is null.</exception>
    public static CompiledPlan Merge(IReadOnlyList<CompiledCondition> conditions)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        var kept = new List<CompiledCondition>(conditions.Count);
        var features = new List<FeatureSpec>();
        var seen = new HashSet<FeatureSpec>();
        var depth = 1;
        var needsContext = false;
        var needsExpiryDays = false;
        foreach (var condition in conditions)
        {
            ArgumentNullException.ThrowIfNull(condition, nameof(conditions));
            kept.Add(condition);
            foreach (var spec in condition.Features)
            {
                if (seen.Add(spec))
                {
                    features.Add(spec);
                }
            }

            depth = Math.Max(depth, condition.HistoryDepth);
            needsContext |= condition.NeedsContext;
            needsExpiryDays |= condition.NeedsExpiryDays;
        }

        return new CompiledPlan(kept.AsReadOnly(), features.AsReadOnly(), depth, needsContext, needsExpiryDays);
    }
}
