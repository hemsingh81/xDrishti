using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Simulation;

/// <summary>
/// The one value the two stages of the simulator share (ADR 0037): a decision to enter, taken at the end of the minute <c>bars[DecisionBarIndex]</c>. It carries no exit policy and no exit field, so that the same entries can be
/// walked under every exit policy (P4). A test builds it by hand; the entry scanner of P3-03a builds it from the feature frame.
/// </summary>
/// <param name="StrategyId">The strategy that decided.</param>
/// <param name="Instrument">The instrument.</param>
/// <param name="Side">Buy or sell.</param>
/// <param name="DecisionBarIndex">Position, in the list of 1-minute bars, of the minute that closed the evaluation bar.</param>
/// <param name="DecisionEpochSeconds">The decision instant: the end of that minute (its start plus 60 seconds).</param>
/// <param name="TriggerTimeframe">The evaluation timeframe.</param>
/// <param name="TriggerBar">The evaluation bar; its epoch is the bucket start.</param>
/// <param name="TriggerBarMinutes">Minutes the bucket holds.</param>
/// <param name="TriggerBarComplete">Every minute of the window was there.</param>
/// <param name="StopLevel">The stop price, used as given.</param>
/// <param name="Trace">Why the decision was taken; never read by the walker.</param>
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
