using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Simulation;

/// <summary>
/// An entry the strategy decided on: which strategy, instrument and side, where the decision was taken in the list of 1-minute bars (<paramref name="DecisionBarIndex"/>, the minute that closed the evaluation bar) and when
/// (<paramref name="DecisionEpochSeconds"/>, the instant that bar closed), the bar that triggered it and the stop level. It carries no exit, so that the same entries can be walked under every exit policy.
/// </summary>
public sealed record EntryCandidate(
    string StrategyId,
    InstrumentRef Instrument,
    Side Side,
    int DecisionBarIndex,
    long DecisionEpochSeconds,
    Timeframe TriggerTimeframe,
    Bar TriggerBar,
    int TriggerBarMinutes,
    bool TriggerBarComplete,
    double StopLevel,
    DecisionTrace Trace);
