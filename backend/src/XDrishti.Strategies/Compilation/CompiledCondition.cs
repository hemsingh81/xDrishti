using System.Globalization;
using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;
using XDrishti.Strategies.Binding;

namespace XDrishti.Strategies.Compilation;

/// <summary>
/// A checked condition and what it needs from the feature frame (spec P3-02, criterion 11): exactly the features its text reads, the history depth it needs, and whether the frame must be given a market context or
/// expiry dates. Immutable and thread-safe. A frame is created from <see cref="Features"/> (or from the merged list of <see cref="ConditionSet.Merge"/>) with at least <see cref="HistoryDepth"/> of history, and
/// <see cref="Bind"/> then resolves the handles of the features in that frame and returns the condition to evaluate. The bound tree behind it is internal.
/// <para>
/// The condition is compiled to a delegate (a <c>System.Linq.Expressions</c> tree built from the bound tree) the first time it is bound, once, whatever the number of frames: checking a condition (the editor's
/// <see cref="ConditionCompiler.Compile"/>) does not pay for it, and a condition that is bound ten thousand times for ten thousand instruments is compiled once. Two threads binding at the same time may both compile it;
/// the results are the same and one is kept.
/// </para>
/// </summary>
public sealed class CompiledCondition
{
    private readonly IBoundExpression _tree;

    private Decision? _decision;

    private ReadList? _reads;

    internal CompiledCondition(
        string source,
        Timeframe evaluationTimeframe,
        IReadOnlyList<FeatureSpec> features,
        int historyDepth,
        bool needsContext,
        bool needsExpiryDays,
        IReadOnlyList<Problem> warnings,
        IBoundExpression tree)
    {
        Source = source;
        EvaluationTimeframe = evaluationTimeframe;
        Features = features;
        HistoryDepth = historyDepth;
        NeedsContext = needsContext;
        NeedsExpiryDays = needsExpiryDays;
        Warnings = warnings;
        _tree = tree;
    }

    /// <summary>The text of the condition as given.</summary>
    public string Source { get; }

    /// <summary>The timeframe the condition was compiled for; the frame needs a feature on it for <c>BarClosed</c> to be asked (a condition that reads none, such as <c>time &gt;= "09:45"</c>, leaves that to the caller).</summary>
    public Timeframe EvaluationTimeframe { get; }

    /// <summary>The distinct features the condition reads, in canonical form, in the order they first appear in the text; the handle of each in a frame made from this list is its position. Empty for a condition that reads no feature.</summary>
    public IReadOnlyList<FeatureSpec> Features { get; }

    /// <summary>The history the frame must keep: the largest lag the condition reads plus 1, at least 1 and at most <see cref="FrameOptions.MaxHistoryDepth"/>.</summary>
    public int HistoryDepth { get; }

    /// <summary>Whether the frame must be created with <see cref="FrameOptions.Context"/>.</summary>
    public bool NeedsContext { get; }

    /// <summary>Whether the frame must be created with <see cref="FrameOptions.ExpiryDays"/>.</summary>
    public bool NeedsExpiryDays { get; }

    /// <summary>The warnings of the compile (a constant condition, a redundant timeframe, a parameter that hides a name), in source order.</summary>
    public IReadOnlyList<Problem> Warnings { get; }

    /// <summary>Whether the condition reads the clock (the <c>time</c> keyword). Such a condition, like one that reads an intraday feature, says nothing about tomorrow from the close of today (P7-04, ADR 0088).</summary>
    public bool ReadsClock => XDrishti.Strategies.Binding.BoundTree.ReadsClock(_tree);

    /// <summary>
    /// Every feature read of the condition in the order of the text, each with the lag it is read at (P3-04d, D5): <c>close[1]</c> is <c>close</c> at lag 1, the previous pair of <c>crosses_above(a, b)</c> is read at the lag the call adds, a
    /// <c>rising(x, n)</c> at the lags 1 to n. A feature written twice is listed twice. At most <see cref="MaxReads"/> reads are listed; <see cref="ReadsTruncated"/> says when more were cut. Computed on first use.
    /// </summary>
    public IReadOnlyList<ConditionRead> Reads => (_reads ??= ReadList.Of(_tree, Features)).Items;

    /// <summary>Whether <see cref="Reads"/> was cut at <see cref="MaxReads"/>.</summary>
    public bool ReadsTruncated => (_reads ??= ReadList.Of(_tree, Features)).Truncated;

    /// <summary>The most reads a condition lists.</summary>
    public const int MaxReads = 64;

    /// <summary>The bound tree: every name resolved, every node typed.</summary>
    internal IBoundExpression Tree => _tree;

    /// <summary>
    /// Resolves the handle of each feature of the condition in <paramref name="frame"/> and returns the condition ready to <see cref="BoundCondition.Evaluate"/>. The frame must have been created from
    /// <see cref="Features"/> or from a list that contains them (the merged list of a <see cref="CompiledPlan"/>), with a history depth of at least <see cref="HistoryDepth"/>; anything else is a programming error and
    /// throws an <see cref="ArgumentException"/> that names the feature or the depth that is missing (a frame that lacks the market context or the expiry dates cannot exist for these features: its creation was
    /// refused). A condition that reads no feature (<c>time &gt;= "09:45"</c>) binds to any frame. The result belongs to this frame's layout: it may be used with every frame whose handles are the same.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is null.</exception>
    /// <exception cref="ArgumentException">The frame was not created from the features of this condition, or keeps less history than it needs.</exception>
    public BoundCondition Bind(FeatureFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var handles = new int[Features.Count];
        for (var i = 0; i < handles.Length; i++)
        {
            var handle = frame.HandleOf(Features[i]);
            if (handle < 0)
            {
                throw new ArgumentException(
                    "The frame was not created from the features of the condition `" + Source + "`: it has no `" + Describe(Features[i]) + "` on " + Features[i].Timeframe.ToCode() + ". Create it from CompiledCondition.Features.",
                    nameof(frame));
            }

            handles[i] = handle;
        }

        if (frame.HistoryDepth < HistoryDepth)
        {
            throw new ArgumentException(
                "The condition `" + Source + "` reads " + (HistoryDepth - 1) + " closed bars back and needs a history depth of " + HistoryDepth + ", but the frame keeps " + frame.HistoryDepth + ". Create it with FrameOptions.HistoryDepth of at least CompiledCondition.HistoryDepth.",
                nameof(frame));
        }

        return new BoundCondition(this, CompiledDelegate(), handles);
    }

    /// <summary>The feature as it is written in a call, <c>rsi(14)</c>, <c>close</c>.</summary>
    internal static string Describe(in FeatureSpec spec)
    {
        if (!IndicatorCatalog.TryGet(spec.Name, out var info) || info.Parameters.Count == 0)
        {
            return spec.Name;
        }

        var arguments = new string[info.Parameters.Count];
        for (var i = 0; i < arguments.Length; i++)
        {
            arguments[i] = spec.Parameter(i).ToString("R", CultureInfo.InvariantCulture);
        }

        return spec.Name + "(" + string.Join(", ", arguments) + ")";
    }

    /// <summary>The compiled delegate, made on first use.</summary>
    private Decision CompiledDelegate()
    {
        var decision = Volatile.Read(ref _decision);
        if (decision is not null)
        {
            return decision;
        }

        var built = ExpressionBuilder.Build(_tree, Features);
        return Interlocked.CompareExchange(ref _decision, built, null) ?? built;
    }
}
