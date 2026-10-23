namespace XDrishti.Domain.Learning;

/// <summary>
/// What the phase check measured by re-reading the two stored runs of the newest experiment and judging them again (P6-18). That is the comparison half of "replay and compare"; the replay that made the candidate run is a separate step.
/// </summary>
/// <param name="ExperimentId">The experiment whose runs were read.</param>
/// <param name="Trades">Trades of the baseline run that were judged.</param>
/// <param name="MeasuredCompareMs">Milliseconds this check spent reading both runs and judging them (measured now).</param>
/// <param name="StoredCompareMs">The milliseconds stored when the experiment was recorded.</param>
/// <param name="StoredOutcome">The outcome stored with the experiment.</param>
/// <param name="MeasuredOutcome">The outcome of judging again with the default rules and the stored knob, trials and hold-out.</param>
/// <param name="SuppliedReplaySeconds">Seconds the owner says a full replay of the candidate took, from a run the owner timed (not measured here, not verified), or null.</param>
public sealed record PhaseReplayFacts(long ExperimentId, int Trades, long MeasuredCompareMs, long StoredCompareMs, string StoredOutcome, string MeasuredOutcome, double? SuppliedReplaySeconds);
