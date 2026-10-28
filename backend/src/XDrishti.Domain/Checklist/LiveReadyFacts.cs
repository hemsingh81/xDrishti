namespace XDrishti.Domain.Checklist;

/// <summary>
/// What the live-ready gate of a manual-live account reads (spec P11-04 criterion 4, ADR 0148). A null fact is "no source could say" and makes the gate unavailable, which is not a pass; the halt, capture and loss sources are built in P11-01 and P11-04b.
/// </summary>
/// <param name="StepSet">The account has a ramp step.</param>
/// <param name="NoOpenHalt">No daily or weekly halt is open; null when not known.</param>
/// <param name="CaptureHealthy">The last session's capture had no gap; null when not known.</param>
/// <param name="LossMeasurable">The loss of the day can be measured; null when not known.</param>
public sealed record LiveReadyFacts(bool StepSet, bool? NoOpenHalt, bool? CaptureHealthy, bool? LossMeasurable);
