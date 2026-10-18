using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;
using XDrishti.Strategies.Compilation;

namespace XDrishti.Strategies.Tests.Support;

/// <summary>A compiled side with the frame made from its feature list (the history depth, the context and the expiry dates it asks for) and bound to it: step a bar, read the evaluation.</summary>
internal sealed class SideRig
{
    private readonly Truth[] _truths;

    private SideRig(CompiledSide side, FeatureFrame frame, BoundSide bound)
    {
        Side = side;
        Frame = frame;
        Bound = bound;
        _truths = new Truth[side.Items.Count];
    }

    public CompiledSide Side { get; }

    public FeatureFrame Frame { get; }

    public BoundSide Bound { get; }

    /// <summary>The truth of every item after the last evaluation, in the order of <see cref="CompiledSide.Items"/>.</summary>
    public IReadOnlyList<Truth> Items => _truths;

    public static SideRig Of(CompiledSide side, MarketContext? context = null, IReadOnlySet<DateOnly>? expiryDays = null, int extraDepth = 0)
    {
        var options = new FrameOptions(
            null,
            side.HistoryDepth + extraDepth,
            side.NeedsContext ? context ?? throw new InvalidOperationException("The side needs a market context.") : context,
            side.NeedsExpiryDays ? expiryDays ?? throw new InvalidOperationException("The side needs expiry dates.") : expiryDays);
        var frame = FeatureFrame.Create(side.Features, options);
        frame.IsSuccess.ShouldBeTrue(frame.IsFailure ? frame.Error.Message + " " + string.Join("; ", frame.Error.Fields?.Select(f => f.Field + ": " + f.Message) ?? []) : string.Empty);
        return new SideRig(side, frame.Value, side.Bind(frame.Value));
    }

    /// <summary>Feeds the bar and evaluates the side (whether or not the evaluation timeframe closed).</summary>
    public SideEvaluation Step(in Bar bar)
    {
        Frame.Step(bar);
        return Evaluate();
    }

    public SideEvaluation Evaluate() => Bound.Evaluate(Frame, _truths);

    /// <summary>The items' truths as letters (<c>TFU</c>).</summary>
    public string Word => Rig.Word(_truths);
}
