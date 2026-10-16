using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Indicators;
using XDrishti.Strategies.Compilation;

namespace XDrishti.Strategies.Tests.Support;

/// <summary>
/// The setup every evaluation test starts from: compile the conditions for a timeframe, merge their plans, create the one frame they share (with the history depth, the context and the expiry dates the plan asks for, and
/// any extra features a test wants the frame to have; <c>close</c> on the evaluation timeframe when the conditions read no feature at all, which a frame cannot be created without), bind each condition and step the frame. <see cref="Step"/> returns the truth of every
/// condition after the bar, and <see cref="Word"/> writes a series of truths as letters (<c>T</c>, <c>F</c>, <c>U</c>) so that a hand-calculated table reads as a string.
/// </summary>
internal sealed class Rig
{
    private readonly BoundCondition[] _bound;

    private Rig(FeatureFrame frame, CompiledPlan plan, BoundCondition[] bound)
    {
        Frame = frame;
        Plan = plan;
        _bound = bound;
    }

    public FeatureFrame Frame { get; }

    public CompiledPlan Plan { get; }

    public IReadOnlyList<BoundCondition> Bound => _bound;

    public static Rig Of(Timeframe timeframe, string source, MarketContext? context = null, IReadOnlySet<DateOnly>? expiryDays = null, IReadOnlyList<FeatureSpec>? extra = null, IReadOnlyDictionary<string, double>? parameters = null, TradingCalendar? calendar = null, int minimumDepth = 1) =>
        Of(timeframe, [source], context, expiryDays, extra, parameters, calendar, minimumDepth);

    public static Rig Of(
        Timeframe timeframe,
        IReadOnlyList<string> sources,
        MarketContext? context = null,
        IReadOnlySet<DateOnly>? expiryDays = null,
        IReadOnlyList<FeatureSpec>? extra = null,
        IReadOnlyDictionary<string, double>? parameters = null,
        TradingCalendar? calendar = null,
        int minimumDepth = 1)
    {
        var conditions = sources.Select(s => Compiling.Condition(s, timeframe, parameters)).ToList();
        var plan = ConditionSet.Merge(conditions);
        var specs = plan.Features.Concat(extra ?? []).Distinct().ToList();
        if (specs.Count == 0)
        {
            // a frame needs a feature; a caller whose conditions read none (time only) adds one on the evaluation timeframe, which it needs for BarClosed anyway
            specs.Add(Specs.Close(timeframe));
        }

        var options = new FrameOptions(calendar, Math.Max(plan.HistoryDepth, minimumDepth), plan.NeedsContext ? context ?? throw new InvalidOperationException("The conditions need a market context.") : context, plan.NeedsExpiryDays ? expiryDays ?? throw new InvalidOperationException("The conditions need expiry dates.") : expiryDays);
        var created = FeatureFrame.Create(specs, options);
        created.IsSuccess.ShouldBeTrue(created.IsFailure ? created.Error.Message + " " + string.Join("; ", created.Error.Fields?.Select(f => f.Field + ": " + f.Message) ?? []) : string.Empty);
        var bound = plan.BindAll(created.Value);
        return new Rig(created.Value, plan, [.. bound]);
    }

    /// <summary>Feeds the bar and returns the truth of every condition afterwards.</summary>
    public Truth[] Step(in Bar bar)
    {
        Frame.Step(bar);
        return Read();
    }

    /// <summary>The truth of every condition now.</summary>
    public Truth[] Read()
    {
        var truths = new Truth[_bound.Length];
        for (var i = 0; i < truths.Length; i++)
        {
            truths[i] = _bound[i].EvaluateTruth(Frame);
        }

        return truths;
    }

    /// <summary>The truth of the first condition after each bar, as letters.</summary>
    public string Run(IEnumerable<Bar> bars) => Word(bars.Select(bar => Step(bar)[0]));

    public static string Word(IEnumerable<Truth> truths) => string.Concat(truths.Select(t => t switch { Truth.True => 'T', Truth.False => 'F', _ => 'U' }));
}
