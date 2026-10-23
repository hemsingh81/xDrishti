namespace XDrishti.Domain.Learning;

/// <summary>What the calibration monitor found for one model (P6-09).</summary>
/// <param name="Subject"><c>model:ID</c>.</param>
/// <param name="Outcomes">Scored outcomes the model had not been trained on.</param>
/// <param name="Events">Alarms that fired or cleared, in order of time.</param>
/// <param name="Note">What a reader must know about which outcomes were read.</param>
public sealed record CalibrationDrift(string Subject, int Outcomes, IReadOnlyList<DriftEvent> Events, string Note);
