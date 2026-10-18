using XDrishti.Indicators;

namespace XDrishti.Strategies.Compilation;

/// <summary>
/// A <see cref="CompiledSide"/> bound to the handles of one feature frame: it evaluates the lists at list level, showing the three-valued result of every item, and says whether the entry window is open. A pure function of
/// the frame as it is now, with no state of its own (a host that restarts rebuilds a frame and replays bars). <b>When</b> to evaluate is the caller's: only at the steps where <c>frame.BarClosed</c> of the evaluation
/// timeframe (the side's frame has a lane for it). Belongs to its frame's thread, like a <see cref="BoundCondition"/>. Allocates nothing.
/// </summary>
public sealed class BoundSide
{
    private readonly BoundCondition[] _conditions;

    private readonly int _setupCount;

    private readonly int _triggerCount;

    internal BoundSide(CompiledSide side, BoundCondition[] conditions)
    {
        Side = side;
        _conditions = conditions;
        _setupCount = side.Setup?.Items.Count ?? 0;
        _triggerCount = side.Trigger.Items.Count;
    }

    /// <summary>The side this was bound from.</summary>
    public CompiledSide Side { get; }

    /// <summary>
    /// Evaluates every item (none is skipped, so a caller can count true, false and unknown per path), writes the truth of each into <paramref name="itemTruths"/> in the order of <see cref="CompiledSide.Items"/> and returns the
    /// truths of the three lists. An <c>all</c> list is the Kleene AND of its items, an <c>any</c> list the Kleene OR; a side without a setup list has a true setup, one without an invalidate list a false invalidation.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="itemTruths"/> is shorter than the number of items.</exception>
    public SideEvaluation Evaluate(FeatureFrame frame, Span<Truth> itemTruths)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (itemTruths.Length < _conditions.Length)
        {
            throw new ArgumentException("The span for the truths of the items is shorter than the " + _conditions.Length + " items of the side.", nameof(itemTruths));
        }

        for (var i = 0; i < _conditions.Length; i++)
        {
            itemTruths[i] = _conditions[i].EvaluateTruth(frame);
        }

        var setup = Side.Setup is { } setupList ? ListFold.Fold(itemTruths.Slice(0, _setupCount), setupList.Mode) : Truth.True;
        var trigger = ListFold.Fold(itemTruths.Slice(_setupCount, _triggerCount), Side.Trigger.Mode);
        var invalidate = Side.Invalidate is { } invalidateList ? ListFold.Fold(itemTruths.Slice(_setupCount + _triggerCount, invalidateList.Items.Count), invalidateList.Mode) : Truth.False;
        return new SideEvaluation(setup, trigger, invalidate);
    }

    /// <summary>
    /// Whether the clock of the step is inside the entry window of the time rules: <c>first_entry ≤ time ≤ last_entry</c>, both inclusive, a missing end open, with <c>time</c> read as the keyword reads it (the start of the
    /// last accepted 1-minute bar plus one minute, ADR 0034 decision 4). False before the first step. It answers for the clock of the <i>step</i>, not of the bar: telling a late bar from yesterday's is the scanner's.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is null.</exception>
    public bool InEntryWindow(FeatureFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var time = Kernel.CloseMinute(frame);
        if (double.IsNaN(time))
        {
            return false;
        }

        var rules = Side.TimeRules;
        return !(rules.FirstEntryMinute is { } first && time < first) && !(rules.LastEntryMinute is { } last && time > last);
    }
}
