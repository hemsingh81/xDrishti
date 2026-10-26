using XDrishti.Domain.Research;

namespace XDrishti.Application.Learning.Jobs;

/// <summary>What a stored run says that a replay needs (spec P8-18 Part B, assumption 5): every field of the <c>SimulationRequest</c> that made it, read back from the stored record. Nothing here is guessed.</summary>
/// <param name="StrategyId">The strategy id.</param>
/// <param name="DefinitionSha256">The hash of the definition the run used.</param>
/// <param name="Parameters">The effective strategy parameters.</param>
/// <param name="Timeframe">The evaluation timeframe code.</param>
/// <param name="From">First date.</param>
/// <param name="To">Last date.</param>
/// <param name="Options">The thresholds of the data notes.</param>
/// <param name="AcknowledgeCsvAdjusted">The CSV acknowledgement.</param>
/// <param name="Fills">The fill rules.</param>
/// <param name="InstrumentIds">The instruments in run order.</param>
/// <param name="BasketId">The basket, for a basket universe.</param>
/// <param name="Costs">Gross or the stored sheet with its capital and profile.</param>
/// <param name="Exit">The one plain exit of the definition, or null when it cannot be read as one exit with numbers.</param>
/// <param name="TimeStop">The time-stop overlay of the exit, or null.</param>
public sealed record ReplayBaseline(
    string StrategyId,
    string DefinitionSha256,
    IReadOnlyDictionary<string, double> Parameters,
    string Timeframe,
    DateOnly From,
    DateOnly To,
    ReplayOptions Options,
    bool AcknowledgeCsvAdjusted,
    FillRules Fills,
    IReadOnlyList<Guid> InstrumentIds,
    Guid? BasketId,
    Application.Research.CostChoice Costs,
    ExitCandidate? Exit,
    TimeStopChoice? TimeStop);
