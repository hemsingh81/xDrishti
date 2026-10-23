namespace XDrishti.Domain.Learning;

/// <summary>An alarm that went on or off (P6-09). The evidence is a fixed sentence with the numbers behind it.</summary>
/// <param name="Detector">The monitor.</param>
/// <param name="Subject">The cell key, or <c>model:ID</c> for the calibration monitor.</param>
/// <param name="Fired">True when the alarm fired, false when it cleared.</param>
/// <param name="AtEpochSeconds">The instant of the check; only trades known by then were read.</param>
/// <param name="Check">The number of the check (or of the trade, for the CUSUM) in the monitored period, from 1.</param>
/// <param name="Value">The statistic at the check.</param>
/// <param name="Limit">The limit it was compared with.</param>
/// <param name="Evidence">The numbers, as text.</param>
public sealed record DriftEvent(DriftDetectorKind Detector, string Subject, bool Fired, long AtEpochSeconds, int Check, double Value, double Limit, string Evidence);
