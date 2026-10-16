using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;
using XDrishti.Strategies.Compilation;

namespace XDrishti.Strategies.Tests.Support.Oracle;

/// <summary>
/// A set of conditions compiled once, ready to be run over many series: each call of <see cref="NewFrame"/> makes a fresh frame (the same features, history depth, context and expiry dates) and binds the conditions to it,
/// so a property can run the same conditions over a full series, over a truncated one and over a perturbed one. The frame always has <c>close</c> on the evaluation timeframe, so that <c>BarClosed(evaluation)</c> can be
/// asked for whatever the conditions read.
/// </summary>
internal sealed class Scenario
{
    private readonly IReadOnlyList<FeatureSpec> _specs;

    private readonly FrameOptions _options;

    private Scenario(Timeframe evaluation, IReadOnlyList<string> sources, CompiledPlan plan, IReadOnlyList<FeatureSpec> specs, FrameOptions options, IReadOnlyList<ContextDay>? contextDays)
    {
        Evaluation = evaluation;
        Sources = sources;
        Plan = plan;
        _specs = specs;
        _options = options;
        ContextDays = contextDays;
        Slowest = specs.Max(s => s.Timeframe);
    }

    public Timeframe Evaluation { get; }

    public IReadOnlyList<string> Sources { get; }

    public CompiledPlan Plan { get; }

    /// <summary>The records the frame's context is built from, when the scenario was given them (so that a run can use a context made of some of them, or of changed ones); null when it has no context or was given a ready one.</summary>
    public IReadOnlyList<ContextDay>? ContextDays { get; }

    /// <summary>The longest timeframe any feature of the frame is on (the evaluation timeframe included).</summary>
    public Timeframe Slowest { get; }

    /// <summary>
    /// Compiles the sources (which must be valid; the failure message shows the text and the problems) and merges them. The frame is made from the features and the depth of the plan, or from <paramref name="features"/> and
    /// <paramref name="historyDepth"/> when given (a test that works them out for itself). The context is <paramref name="context"/>, or built from <paramref name="contextDays"/> (which the scenario then keeps, see
    /// <see cref="WithContextDays"/>).
    /// </summary>
    public static Scenario Create(
        Timeframe evaluation,
        IReadOnlyList<string> sources,
        MarketContext? context = null,
        IReadOnlySet<DateOnly>? expiryDays = null,
        IReadOnlyList<FeatureSpec>? features = null,
        int? historyDepth = null,
        IReadOnlyList<ContextDay>? contextDays = null)
    {
        var compiled = sources.Select(s => Compiling.Condition(s, evaluation, ModelGenerator.Parameters)).ToList();
        var plan = ConditionSet.Merge(compiled);
        var specs = (features ?? plan.Features).Append(Specs.Close(evaluation)).Distinct().ToList();
        var options = new FrameOptions(null, historyDepth ?? plan.HistoryDepth, context ?? (contextDays is null ? null : OracleContext.Build(contextDays)), expiryDays);
        return new Scenario(evaluation, sources, plan, specs, options, contextDays);
    }

    /// <summary>The same conditions, features and expiry dates with a context built from other records (they replace those the scenario was given; the plan is not compiled again).</summary>
    public Scenario WithContextDays(IReadOnlyList<ContextDay> days) => new(Evaluation, Sources, Plan, _specs, _options with { Context = OracleContext.Build(days) }, days);

    /// <summary>A fresh frame and the conditions bound to it, in the order of <see cref="Sources"/>.</summary>
    public (FeatureFrame Frame, IReadOnlyList<BoundCondition> Bound) NewFrame()
    {
        var created = FeatureFrame.Create(_specs, _options);
        created.IsSuccess.ShouldBeTrue(created.IsFailure ? created.Error.Message + " " + string.Join("; ", created.Error.Fields?.Select(f => f.Field + ": " + f.Message) ?? []) : string.Empty);
        return (created.Value, Plan.BindAll(created.Value));
    }
}
