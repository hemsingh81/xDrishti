using XDrishti.Domain.Research;

namespace XDrishti.Application.Research;

/// <summary>
/// The request of a simulation run (spec P3-04a, D1): a built-in strategy, the window, and exactly one universe, an explicit list of instruments or a basket. All dates are India dates, inclusive. Nothing here is a time of day or a
/// random seed: equal requests on equal stored data give equal records.
/// </summary>
/// <param name="StrategyId">A built-in strategy id (<c>ORB</c>); the built-in factory of the engine knows it.</param>
/// <param name="Parameters">The strategy's parameters by name; null or empty means the defaults. Every value is finite.</param>
/// <param name="EvaluationTimeframe">The timeframe the rules are evaluated on, the code of <c>Timeframe</c> (<c>5m</c> or <c>15m</c> for ORB).</param>
/// <param name="From">First date of the window; the year is 2000 to 2099 (the importer's range).</param>
/// <param name="To">Last date of the window, not before <paramref name="From"/>.</param>
/// <param name="InstrumentIds">One to 2,000 instrument ids, run in the order given, no repeats; exclusive with <paramref name="BasketId"/>.</param>
/// <param name="BasketId">A manual basket that is not archived; its members now, in the basket's order.</param>
/// <param name="Costs">What the run is charged with (spec P3-03f, F1): a stored charge sheet or gross, by name. Required, so that a call site cannot forget it; part of the run key through the record.</param>
/// <param name="AcknowledgeCsvAdjusted">The owner's consent to run an instrument whose CSV prices are already adjusted for an action that would adjust them again (ADR 0017 §5); part of the run key.</param>
/// <param name="Options">The thresholds of the data notes; null means the defaults (0.90 and 0.20).</param>
/// <param name="Fills">The fill rules (P3-03b); null means <see cref="FillRules.Default"/>.</param>
public sealed record SimulationRequest(
    string StrategyId,
    IReadOnlyDictionary<string, double>? Parameters,
    string EvaluationTimeframe,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<Guid>? InstrumentIds,
    Guid? BasketId,
    CostChoice Costs,
    bool AcknowledgeCsvAdjusted = false,
    ReplayOptions? Options = null,
    FillRulesInput? Fills = null);
