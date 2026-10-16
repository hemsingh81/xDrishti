using XDrishti.Indicators;

namespace XDrishti.Strategies.Compilation;

/// <summary>
/// A <see cref="CompiledCondition"/> bound to the handles of one feature frame (spec P3-02, <i>Contract</i>): <see cref="Evaluate"/> and <see cref="EvaluateTruth"/> read the frame as it is <i>now</i> — the last closed
/// bar of each feature's timeframe, as the frame's rule says — and nothing else; they are pure, allocate nothing and never throw for a value (a NaN, a division by zero, a feature that is not ready, a context
/// with a gap): such a thing is <see cref="Truth.Unknown"/>. <b>When</b> to evaluate is the caller's business (a simulator evaluates at the steps where <c>frame.BarClosed(evaluationTimeframe)</c>).
/// <para>
/// The handles are those of the frame it was bound to. It may be evaluated with any other frame that has the same layout — created from the same list of features and with the same history depth, as when one
/// strategy runs over many instruments —, but not with a frame of another layout: that reads other features without any error. Thread-confined to the frame's thread, like the frame.
/// </para>
/// </summary>
public sealed class BoundCondition
{
    private readonly Decision _decision;

    private readonly int[] _handles;

    internal BoundCondition(CompiledCondition condition, Decision decision, int[] handles)
    {
        Condition = condition;
        _decision = decision;
        _handles = handles;
    }

    /// <summary>The condition this was bound from.</summary>
    public CompiledCondition Condition { get; }

    /// <summary>The decision: true only when the condition is <see cref="Truth.True"/>. <see cref="Truth.Unknown"/> is false, and so is <c>not</c> of an unknown. Allocates nothing.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is null.</exception>
    public bool Evaluate(FeatureFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return _decision(frame, _handles) == Kernel.True;
    }

    /// <summary>The three-valued result: <see cref="Truth.True"/>, <see cref="Truth.False"/>, or <see cref="Truth.Unknown"/> when an operand it needed is not ready. Allocates nothing.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is null.</exception>
    public Truth EvaluateTruth(FeatureFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return (Truth)_decision(frame, _handles);
    }
}
