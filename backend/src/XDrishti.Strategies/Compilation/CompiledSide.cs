using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Indicators;
using XDrishti.Strategies.Model;

namespace XDrishti.Strategies.Compilation;

/// <summary>
/// One side of a strategy compiled for one evaluation timeframe (spec P3-02b): every condition of every list compiled as its own condition, the stop, the exits and overlays as plans, the time rules as minutes, the single
/// feature list a frame is created from (<see cref="Features"/>: the features of the conditions in the order of the model, then the stop's, then the exits', then a <c>close</c> on the evaluation timeframe when no feature is
/// on it, because a frame can be asked <c>BarClosed</c> only for a timeframe it has a feature on and cannot be created without a feature), the history depth the frame must keep and what it must be given. Immutable and
/// thread-safe; any number of frames may be bound to it. Create the frame with <see cref="Features"/> and a <see cref="FrameOptions.HistoryDepth"/> of at least <see cref="HistoryDepth"/> (and the context and expiry dates
/// when the side says so), then <see cref="Bind"/>.
/// </summary>
public sealed class CompiledSide
{
    internal CompiledSide(
        string strategyId,
        Side side,
        Style style,
        Timeframe evaluationTimeframe,
        CompiledList? setup,
        CompiledList trigger,
        CompiledList? invalidate,
        EntryKind entry,
        StopPlan stop,
        IReadOnlyList<ExitPlan> exits,
        IReadOnlyList<OverlayPlan> overlays,
        SessionRules timeRules,
        IReadOnlyList<string> filters,
        IReadOnlyList<FeatureSpec> features,
        int historyDepth,
        bool needsContext,
        bool needsExpiryDays,
        IReadOnlyList<LocatedProblem> warnings)
    {
        StrategyId = strategyId;
        Side = side;
        Style = style;
        EvaluationTimeframe = evaluationTimeframe;
        Setup = setup;
        Trigger = trigger;
        Invalidate = invalidate;
        Entry = entry;
        Stop = stop;
        Exits = exits;
        Overlays = overlays;
        TimeRules = timeRules;
        Filters = filters;
        Features = features;
        HistoryDepth = historyDepth;
        NeedsContext = needsContext;
        NeedsExpiryDays = needsExpiryDays;
        Warnings = warnings;
        var items = new List<CompiledItem>();
        foreach (var list in new[] { setup, trigger, invalidate })
        {
            if (list is not null)
            {
                items.AddRange(list.Items);
            }
        }

        Items = items.AsReadOnly();
    }

    /// <summary>The id of the strategy.</summary>
    public string StrategyId { get; }

    /// <summary>The side that was compiled.</summary>
    public Side Side { get; }

    /// <summary>The style of the strategy.</summary>
    public Style Style { get; }

    /// <summary>The timeframe the side was compiled for: where an unqualified feature is read, what <c>time</c> means, and the timeframe the caller evaluates at (the steps where <c>frame.BarClosed</c> of it).</summary>
    public Timeframe EvaluationTimeframe { get; }

    /// <summary>The setup list, or null when the side has none (no gate).</summary>
    public CompiledList? Setup { get; }

    /// <summary>The trigger list.</summary>
    public CompiledList Trigger { get; }

    /// <summary>The invalidate list, or null when the side has none (no invalidation).</summary>
    public CompiledList? Invalidate { get; }

    /// <summary>The items of the setup, then the trigger, then the invalidate list: the order of the truths <see cref="BoundSide.Evaluate"/> writes.</summary>
    public IReadOnlyList<CompiledItem> Items { get; }

    /// <summary>How the entry is placed.</summary>
    public EntryKind Entry { get; }

    /// <summary>The stop.</summary>
    public StopPlan Stop { get; }

    /// <summary>The candidate exit policies, in the order of the model.</summary>
    public IReadOnlyList<ExitPlan> Exits { get; }

    /// <summary>The overlay exits (empty when none).</summary>
    public IReadOnlyList<OverlayPlan> Overlays { get; }

    /// <summary>The time rules as minutes of the India day.</summary>
    public SessionRules TimeRules { get; }

    /// <summary>The filter names of the strategy (known ones, not evaluated in P3: the simulator refuses a run whose list is not empty).</summary>
    public IReadOnlyList<string> Filters { get; }

    /// <summary>The distinct features the frame is created from, in this order: the handle of each is its position.</summary>
    public IReadOnlyList<FeatureSpec> Features { get; }

    /// <summary>The history the frame must keep.</summary>
    public int HistoryDepth { get; }

    /// <summary>Whether the frame must be created with <see cref="FrameOptions.Context"/>.</summary>
    public bool NeedsContext { get; }

    /// <summary>Whether the frame must be created with <see cref="FrameOptions.ExpiryDays"/>.</summary>
    public bool NeedsExpiryDays { get; }

    /// <summary>The warnings of the compile, located (a redundant timeframe in an item, a filter that is not evaluated, a parameter that shadows a name).</summary>
    public IReadOnlyList<LocatedProblem> Warnings { get; }

    /// <summary>
    /// This side with the exits of <paramref name="others"/> added to its own, for one scan that serves several candidate exits (P4-05, D2): the entry, the stop, the time rules and the history depth are this side's, <see cref="Exits"/> holds every
    /// candidate's plan and <see cref="Features"/> holds this side's features followed by the features only the other candidates read, so a frame created from it records the tape of the union. The others must be the same side of the same
    /// strategy compiled with another exit; this is not checked.
    /// </summary>
    /// <param name="others">The same side compiled with the other candidates' exits.</param>
    /// <exception cref="ArgumentNullException"><paramref name="others"/> is null.</exception>
    public CompiledSide WithExitsOf(IEnumerable<CompiledSide> others)
    {
        ArgumentNullException.ThrowIfNull(others);
        var exits = new List<ExitPlan>(Exits);
        var features = new List<FeatureSpec>(Features);
        var seen = new HashSet<FeatureSpec>(features);
        foreach (var other in others)
        {
            exits.AddRange(other.Exits);
            foreach (var spec in other.Exits.SelectMany(e => e.Reads).Where(seen.Add))
            {
                features.Add(spec);
            }
        }

        return new CompiledSide(StrategyId, Side, Style, EvaluationTimeframe, Setup, Trigger, Invalidate, Entry, Stop, exits.AsReadOnly(), Overlays, TimeRules, Filters, features.AsReadOnly(), HistoryDepth, NeedsContext, NeedsExpiryDays, Warnings);
    }

    /// <summary>
    /// Binds every condition to the frame and returns the side ready to <see cref="BoundSide.Evaluate"/>. The layout is checked here, because a bound condition alone cannot (ADR 0034, Consequences): the frame must have been
    /// created from <see cref="Features"/> in this order (a longer list that starts with them is fine) and keep at least <see cref="HistoryDepth"/> closed bars; anything else is a programming error and throws an
    /// <see cref="ArgumentException"/> that names the feature or the depth.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is null.</exception>
    /// <exception cref="ArgumentException">The frame has the features in another order, lacks one, or keeps less history than the side needs.</exception>
    public BoundSide Bind(FeatureFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        for (var i = 0; i < Features.Count; i++)
        {
            var handle = frame.HandleOf(Features[i]);
            if (handle != i)
            {
                throw new ArgumentException(
                    handle < 0
                        ? "The frame was not created from the features of the side `" + StrategyId + "`: it has no `" + CompiledCondition.Describe(Features[i]) + "` on " + Features[i].Timeframe.ToCode() + ". Create it from CompiledSide.Features."
                        : "The frame has the features of the side `" + StrategyId + "` in another order: `" + CompiledCondition.Describe(Features[i]) + "` on " + Features[i].Timeframe.ToCode() + " is feature " + handle + " of the frame, but feature " + i + " of the side. Create it from CompiledSide.Features, in that order.",
                    nameof(frame));
            }
        }

        if (frame.HistoryDepth < HistoryDepth)
        {
            throw new ArgumentException(
                "The side `" + StrategyId + "` needs a history depth of " + HistoryDepth + ", but the frame keeps " + frame.HistoryDepth + ". Create it with FrameOptions.HistoryDepth of at least CompiledSide.HistoryDepth.",
                nameof(frame));
        }

        var bound = new BoundCondition[Items.Count];
        for (var i = 0; i < bound.Length; i++)
        {
            bound[i] = Items[i].Condition.Bind(frame);
        }

        return new BoundSide(this, bound);
    }
}
