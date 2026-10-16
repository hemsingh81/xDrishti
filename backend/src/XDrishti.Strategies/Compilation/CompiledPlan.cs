using XDrishti.Indicators;

namespace XDrishti.Strategies.Compilation;

/// <summary>
/// What one feature frame must provide for <i>several</i> conditions (spec P3-02, <i>Contract</i>, made by <see cref="ConditionSet.Merge"/>): the union of their features without duplicates, the largest history depth,
/// and whether any of them needs a market context or expiry dates. A strategy has several conditions (a setup, a trigger, an exit …) and runs them over one frame: create the frame from <see cref="Features"/> with a
/// <see cref="FrameOptions.HistoryDepth"/> of at least <see cref="HistoryDepth"/> (and the context or expiry dates when the plan says so), then <see cref="BindAll"/>. Each condition keeps its own bound tree and its own
/// delegate; they share nothing but the frame. Immutable and thread-safe.
/// </summary>
public sealed class CompiledPlan
{
    internal CompiledPlan(IReadOnlyList<CompiledCondition> conditions, IReadOnlyList<FeatureSpec> features, int historyDepth, bool needsContext, bool needsExpiryDays)
    {
        Conditions = conditions;
        Features = features;
        HistoryDepth = historyDepth;
        NeedsContext = needsContext;
        NeedsExpiryDays = needsExpiryDays;
    }

    /// <summary>The merged conditions, in the order they were given.</summary>
    public IReadOnlyList<CompiledCondition> Conditions { get; }

    /// <summary>The distinct features all the conditions read (specs are canonical, so two spellings of one feature are one), in the order they first appear: the conditions in order, the features of each in its own order. Empty when no condition reads a feature.</summary>
    public IReadOnlyList<FeatureSpec> Features { get; }

    /// <summary>The history the frame must keep: the largest <see cref="CompiledCondition.HistoryDepth"/> of the conditions (1 when there are none).</summary>
    public int HistoryDepth { get; }

    /// <summary>Whether any condition needs the frame to be created with <see cref="FrameOptions.Context"/>.</summary>
    public bool NeedsContext { get; }

    /// <summary>Whether any condition needs the frame to be created with <see cref="FrameOptions.ExpiryDays"/>.</summary>
    public bool NeedsExpiryDays { get; }

    /// <summary>Binds every condition to the frame, separately, in the order of <see cref="Conditions"/> (see <see cref="CompiledCondition.Bind"/>; the same exceptions).</summary>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is null.</exception>
    /// <exception cref="ArgumentException">The frame was not created from these features, or keeps less history than a condition needs.</exception>
    public IReadOnlyList<BoundCondition> BindAll(FeatureFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var bound = new List<BoundCondition>(Conditions.Count);
        foreach (var condition in Conditions)
        {
            bound.Add(condition.Bind(frame));
        }

        return bound.AsReadOnly();
    }
}
